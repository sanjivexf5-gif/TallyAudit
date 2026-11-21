using System.Globalization;
using System.Text;
using System.Threading;

namespace TallyAuditAssistant.Engine.Gst;

/// <summary>
/// Parses a documented, normalized IMS snapshot CSV. Column mapping is explicit so
/// an unfamiliar GSTN export is not silently interpreted as a different field layout.
/// </summary>
public static class GstImsActionSnapshotCsvParser
{
    public static IReadOnlyList<GstImsInvoiceAction> Parse(string csv, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(csv))
            throw new InvalidDataException("IMS CSV is empty.");

        var rows = ReadRows(csv, cancellationToken);
        if (rows.Count < 2)
            throw new InvalidDataException("IMS CSV must contain a header and at least one invoice row.");

        var normalizedHeaders = rows[0].Select((value, index) => (Key: NormalizeHeader(value), Index: index))
            .Where(x => x.Key.Length > 0)
            .ToArray();
        var duplicateHeaders = normalizedHeaders
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        if (duplicateHeaders.Length > 0)
            throw new InvalidDataException(
                $"IMS CSV contains duplicate column headers: {string.Join(", ", duplicateHeaders)}.");

        var headers = normalizedHeaders.ToDictionary(x => x.Key, x => x.Index,
            StringComparer.OrdinalIgnoreCase);

        int Column(params string[] names)
        {
            foreach (var name in names)
                if (headers.TryGetValue(NormalizeHeader(name), out var index)) return index;
            return -1;
        }

        var gstinColumn = Column("Supplier GSTIN", "GSTIN of Supplier", "GSTIN");
        var invoiceColumn = Column("Invoice Number", "Invoice No", "Document Number");
        var dateColumn = Column("Invoice Date", "Document Date");
        var valueColumn = Column("Invoice Value", "Document Value");
        var actionColumn = Column("IMS Action", "Action Taken", "Action", "Supplier Action");
        var documentTypeColumn = Column("Document Type", "Type");
        var referenceColumn = Column("Reference ID", "Reference Number");

        if (gstinColumn < 0 || invoiceColumn < 0 || valueColumn < 0 || actionColumn < 0)
            throw new InvalidDataException(
                "Required IMS CSV columns missing: supplier GSTIN, invoice number, invoice value and IMS action.");

        string Cell(IReadOnlyList<string> row, int column) =>
            column >= 0 && column < row.Count ? row[column].Trim() : string.Empty;

        var result = new List<GstImsInvoiceAction>();
        for (var i = 1; i < rows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[i];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            if (row.Count > rows[0].Count)
                throw new InvalidDataException(
                    $"IMS CSV row {i + 1} contains more fields than the header.");
            var gstin = Cell(row, gstinColumn);
            var invoice = Cell(row, invoiceColumn);
            if (gstin.Length == 0 && invoice.Length == 0) continue;
            var valueText = Cell(row, valueColumn);
            if (!TryAmount(valueText, out var value))
                throw new InvalidDataException($"Invalid invoice value on IMS CSV row {i + 1}.");
            var dateText = Cell(row, dateColumn);
            DateTime? date = null;
            if (dateText.Length > 0)
            {
                string[] formats = ["dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "d-M-yyyy", "d/M/yyyy"];
                if (!DateTime.TryParseExact(dateText, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed) &&
                    !DateTime.TryParse(dateText, CultureInfo.GetCultureInfo("en-IN"),
                        DateTimeStyles.None, out parsed))
                    throw new InvalidDataException($"Invalid invoice date on IMS CSV row {i + 1}.");
                date = parsed.Date;
            }

            result.Add(new GstImsInvoiceAction(gstin, invoice, date, value,
                ParseAction(Cell(row, actionColumn)), NullIfEmpty(Cell(row, documentTypeColumn)),
                NullIfEmpty(Cell(row, referenceColumn))));
        }

        if (result.Count == 0) throw new InvalidDataException("No invoice rows found in IMS CSV.");
        return result;
    }

    private static GstImsSupplierAction ParseAction(string value) =>
        NormalizeHeader(value) switch
        {
            "ACCEPT" or "ACCEPTED" => GstImsSupplierAction.Accepted,
            "REJECT" or "REJECTED" => GstImsSupplierAction.Rejected,
            "PENDING" => GstImsSupplierAction.Pending,
            "NOACTION" or "NOACTIONTAKEN" => GstImsSupplierAction.NoAction,
            "DEEMEDACCEPTED" => GstImsSupplierAction.DeemedAccepted,
            _ => GstImsSupplierAction.Unknown
        };

    private static bool TryAmount(string value, out decimal amount)
    {
        var normalized = value.Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("₹", string.Empty, StringComparison.Ordinal).Trim();
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private static string NormalizeHeader(string value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant).ToArray());

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static List<List<string>> ReadRows(string csv, CancellationToken cancellationToken)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldClosed = false;
        csv = csv.TrimStart('\uFEFF');

        for (var i = 0; i < csv.Length; i++)
        {
            if ((i & 0x0FFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var c = csv[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                    fieldClosed = true;
                }
                else field.Append(c);
                continue;
            }

            if (c == '"')
            {
                if (field.Length != 0 || fieldClosed)
                    throw new InvalidDataException("IMS CSV contains a quote inside an unquoted field.");
                inQuotes = true;
            }
            else if (c == ',' )
            {
                row.Add(field.ToString());
                field.Clear();
                fieldClosed = false;
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                fieldClosed = false;
                if (row.Any(x => !string.IsNullOrWhiteSpace(x))) rows.Add(row);
                row = [];
            }
            else
            {
                if (fieldClosed && !char.IsWhiteSpace(c))
                    throw new InvalidDataException("IMS CSV contains trailing characters after a quoted field.");
                if (!fieldClosed) field.Append(c);
            }
        }

        if (inQuotes) throw new InvalidDataException("IMS CSV has an unterminated quoted field.");
        row.Add(field.ToString());
        if (row.Any(x => !string.IsNullOrWhiteSpace(x))) rows.Add(row);
        return rows;
    }
}
