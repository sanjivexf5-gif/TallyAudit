using System.Text.RegularExpressions;

namespace TallyAuditAssistant.Engine.Gst;

/// <summary>
/// Builds a traceable GSTR-1 outward-supply summary from explicitly classified source rows.
/// It does not infer statutory classifications or submit returns to GSTN.
/// </summary>
public sealed class Gstr1WorkpaperService
{
    private static readonly Regex GstinPattern = new("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const decimal RoundingTolerance = 1.00m;

    public Gstr1Workpaper Prepare(DateTime fromDate, DateTime toDate, IEnumerable<Gstr1OutwardSupply> sourceRows)
    {
        if (fromDate.Date > toDate.Date) throw new ArgumentException("From date must not exceed to date.");
        ArgumentNullException.ThrowIfNull(sourceRows);
        var rows = sourceRows.ToList();
        var issues = new List<Gstr1ValidationIssue>();
        var accepted = new List<Gstr1OutwardSupply>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            void Issue(string code, string message) => issues.Add(new(row.VoucherId, row.InvoiceNumber, code, message));
            if (row.InvoiceDate.Date < fromDate.Date || row.InvoiceDate.Date > toDate.Date)
            { Issue("OUTSIDE_PERIOD", "Invoice date is outside the selected return period."); continue; }
            if (string.IsNullOrWhiteSpace(row.VoucherId) || string.IsNullOrWhiteSpace(row.InvoiceNumber))
            { Issue("MISSING_DOCUMENT_ID", "Voucher ID and invoice number are required."); continue; }
            var duplicateKey = $"{row.Category}|{row.InvoiceNumber.Trim()}|{row.InvoiceDate:yyyyMMdd}";
            if (!seen.Add(duplicateKey)) { Issue("DUPLICATE_DOCUMENT", "Duplicate invoice number and date within the supply category."); continue; }
            if (row.TaxRate < 0 || row.TaxRate > 100 || decimal.Round(row.TaxRate, 2) != row.TaxRate)
            { Issue("INVALID_TAX_RATE", "Tax rate must be between 0 and 100 with at most two decimal places."); continue; }
            if (row.TaxableValue < 0 || row.Igst < 0 || row.Cgst < 0 || row.Sgst < 0 || row.Cess < 0)
            { Issue("NEGATIVE_AMOUNT", "Negative amounts require a separately classified credit/debit note workflow."); continue; }
            if (row.Category is Gstr1SupplyCategory.B2B or Gstr1SupplyCategory.B2CL or Gstr1SupplyCategory.B2CS or Gstr1SupplyCategory.DeemedExport)
            {
                if (string.IsNullOrWhiteSpace(row.PlaceOfSupplyCode) || row.PlaceOfSupplyCode.Length != 2 || !row.PlaceOfSupplyCode.All(char.IsDigit))
                { Issue("INVALID_POS", "A two-digit place-of-supply state code is required."); continue; }
            }
            if (row.Category == Gstr1SupplyCategory.B2B && (string.IsNullOrWhiteSpace(row.RecipientGstin) || !GstinPattern.IsMatch(row.RecipientGstin.Trim())))
            { Issue("INVALID_RECIPIENT_GSTIN", "B2B supply requires a structurally valid recipient GSTIN."); continue; }
            if (row.Category != Gstr1SupplyCategory.B2B && !string.IsNullOrWhiteSpace(row.RecipientGstin) && !GstinPattern.IsMatch(row.RecipientGstin.Trim()))
            { Issue("INVALID_RECIPIENT_GSTIN", "Recipient GSTIN format is invalid."); continue; }
            var calculatedTax = decimal.Round(row.TaxableValue * row.TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
            var reportedTax = row.Igst + row.Cgst + row.Sgst;
            if (Math.Abs(calculatedTax - reportedTax) > RoundingTolerance)
            { Issue("TAX_AMOUNT_MISMATCH", $"Reported GST ({reportedTax:N2}) differs from rate-derived GST ({calculatedTax:N2}) by more than ₹{RoundingTolerance:N2}; verify cess and rounding."); continue; }
            if (row.Igst > 0 && (row.Cgst > 0 || row.Sgst > 0))
            { Issue("MIXED_TAX_HEADS", "IGST and CGST/SGST are both populated; verify place-of-supply treatment."); continue; }
            if (row.Cgst > 0 && Math.Abs(row.Cgst - row.Sgst) > RoundingTolerance)
            { Issue("INTRA_STATE_SPLIT", "CGST and SGST differ by more than the permitted workpaper tolerance."); continue; }
            if (row.IsCreditNote)
            { Issue("CREDIT_NOTE_REVIEW", "Credit notes are not netted into this invoice summary; process through the dedicated note table after review."); continue; }
            accepted.Add(row);
        }
        var summaries = accepted.GroupBy(x => new { x.Category, Pos = (x.PlaceOfSupplyCode ?? string.Empty).Trim(), x.TaxRate })
            .OrderBy(x => x.Key.Category).ThenBy(x => x.Key.Pos).ThenBy(x => x.Key.TaxRate)
            .Select(g => new Gstr1TaxSummary(g.Key.Category, g.Key.Pos, g.Key.TaxRate, g.Count(),
                decimal.Round(g.Sum(x => x.TaxableValue), 2), decimal.Round(g.Sum(x => x.Igst), 2),
                decimal.Round(g.Sum(x => x.Cgst), 2), decimal.Round(g.Sum(x => x.Sgst), 2), decimal.Round(g.Sum(x => x.Cess), 2)))
            .ToList();
        return new Gstr1Workpaper { FromDate = fromDate.Date, ToDate = toDate.Date, SourceRowCount = rows.Count,
            IncludedRowCount = accepted.Count, Summaries = summaries, Issues = issues };
    }
}
