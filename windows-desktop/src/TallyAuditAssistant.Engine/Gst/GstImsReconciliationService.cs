using System.Globalization;
using Dapper;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Gst;

/// <summary>
/// Loads purchase vouchers for one explicitly selected Tally company and period,
/// then reviews a normalized IMS CSV snapshot. This is read-only and does not
/// connect to GSTN or submit/alter IMS actions.
/// </summary>
public sealed class GstImsReconciliationService
{
    public const long MaxCsvFileSizeBytes = 25L * 1024 * 1024;

    private readonly SqliteConnectionFactory _factory;
    private readonly GstImsActionReviewService _reviewService;

    public GstImsReconciliationService(SqliteConnectionFactory factory, GstImsActionReviewService? reviewService = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _reviewService = reviewService ?? new GstImsActionReviewService();
    }

    public async Task<GstImsActionReviewReport> ReviewCsvAsync(
        string companyId, DateTime fromDate, DateTime toDate, string csvPath,
        decimal valueTolerance = 1m, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyId)) throw new ArgumentException("An active Tally company is required.", nameof(companyId));
        if (fromDate.Date > toDate.Date) throw new ArgumentException("Period start date must not be after its end date.");
        if (valueTolerance < 0) throw new ArgumentOutOfRangeException(nameof(valueTolerance));
        if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
            throw new FileNotFoundException("IMS CSV file was not found.", csvPath);

        var fileInfo = new FileInfo(csvPath);
        if (fileInfo.Length > MaxCsvFileSizeBytes)
            throw new InvalidDataException("IMS CSV exceeds the 25 MB import limit. Split the export into smaller period files and import them separately.");

        var csv = await File.ReadAllTextAsync(csvPath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var sourceRows = await Task.Run(() => GstImsActionSnapshotCsvParser.Parse(csv), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var periodRows = new List<GstImsInvoiceAction>();
        var periodIssues = new List<GstImsValidationIssue>();
        for (var i = 0; i < sourceRows.Count; i++)
        {
            var row = sourceRows[i];
            if (row.InvoiceDate is null)
            {
                periodRows.Add(row);
                periodIssues.Add(new(i + 1, "MISSING_INVOICE_DATE",
                    "Invoice date is missing; this row cannot be confirmed as belonging to the selected review period."));
            }
            else if (row.InvoiceDate.Value.Date < fromDate.Date || row.InvoiceDate.Value.Date > toDate.Date)
            {
                periodIssues.Add(new(i + 1, "OUTSIDE_SELECTED_PERIOD",
                    "Invoice date falls outside the selected review period; the row was excluded from matching."));
            }
            else periodRows.Add(row);
        }

        using var db = await _factory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT COALESCE(NULLIF(TRIM(v.ReferenceNumber),''), NULLIF(TRIM(v.VoucherNumber),'')) AS InvoiceNumber,
                   v.VoucherNumber, v.VoucherDate, v.TotalAmount AS InvoiceValue,
                   l.GSTIN AS SupplierGstin, v.VoucherTypeName AS DocumentType
            FROM Vouchers v
            LEFT JOIN Ledgers l ON l.CompanyId=v.CompanyId AND l.Name=v.PartyLedgerName
            WHERE v.CompanyId=@CompanyId AND v.VoucherTypeName LIKE '%Purchase%'
              AND COALESCE(v.IsCancelled,0)=0
              AND DATE(v.VoucherDate) BETWEEN DATE(@FromDate) AND DATE(@ToDate)
            ORDER BY v.VoucherDate, v.VoucherNumber;";
        var rows = await db.QueryAsync(new CommandDefinition(sql, new
        {
            CompanyId = companyId,
            FromDate = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ToDate = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        }, cancellationToken: cancellationToken));

        var books = rows.Select(row => new GstImsBooksInvoice(
            row.SupplierGstin?.ToString() ?? string.Empty,
            row.InvoiceNumber?.ToString() ?? string.Empty,
            ParseDate(row.VoucherDate),
            Convert.ToDecimal(row.InvoiceValue ?? 0m, CultureInfo.InvariantCulture),
            row.VoucherNumber?.ToString(),
            row.DocumentType?.ToString())).ToArray();

        var report = _reviewService.Review(periodRows, books, valueTolerance);
        return new GstImsActionReviewReport
        {
            SourceRows = sourceRows.Count,
            ValidRows = report.ValidRows,
            Items = report.Items,
            Issues = report.Issues.Concat(periodIssues).OrderBy(x => x.SourceRow).ToArray()
        };
    }

    private static DateTime? ParseDate(object? value)
    {
        if (value is DateTime date) return date.Date;
        if (value is null) return null;
        return DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var parsed) ? parsed.Date : null;
    }
}
