using System.Text.RegularExpressions;

namespace TallyAuditAssistant.Engine.Gst;

/// <summary>
/// Builds a traceable GSTR-1 outward-supply workpaper. Debit and credit notes are retained
/// separately and applied with their statutory direction only in net totals. No GSTN filing.
/// </summary>
public sealed class Gstr1WorkpaperService
{
    private static readonly Regex GstinPattern = new("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const decimal RoundingTolerance = 1.00m;

    public Gstr1Workpaper Prepare(DateTime fromDate, DateTime toDate,
        IEnumerable<Gstr1OutwardSupply> sourceRows,
        IEnumerable<Gstr1AdjustmentNote>? adjustmentNotes = null)
    {
        if (fromDate.Date > toDate.Date) throw new ArgumentException("From date must not exceed to date.");
        ArgumentNullException.ThrowIfNull(sourceRows);
        var rows = sourceRows.ToList();
        var notes = (adjustmentNotes ?? Array.Empty<Gstr1AdjustmentNote>()).ToList();
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
            if (!ValidateCategory(row.Category, row.RecipientGstin, row.PlaceOfSupplyCode, out var categoryError))
            { Issue(categoryError.Code, categoryError.Message); continue; }
            if (!ValidateAmounts(row.TaxRate, row.TaxableValue, row.Igst, row.Cgst, row.Sgst, row.Cess, out var amountError))
            { Issue(amountError.Code, amountError.Message); continue; }
            if (row.IsCreditNote)
            { Issue("LEGACY_CREDIT_NOTE_REVIEW", "Use the dedicated adjustment-note input so the original invoice and note direction remain traceable."); continue; }
            accepted.Add(row);
        }

        var acceptedNotes = new List<Gstr1AdjustmentNote>();
        var seenNotes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in notes)
        {
            void Issue(string code, string message) => issues.Add(new(note.VoucherId, note.NoteNumber, code, message));
            if (string.IsNullOrWhiteSpace(note.VoucherId) || string.IsNullOrWhiteSpace(note.NoteNumber) ||
                string.IsNullOrWhiteSpace(note.OriginalInvoiceNumber))
            { Issue("MISSING_NOTE_REFERENCE", "Voucher ID, note number and original invoice number are required."); continue; }
            if (!Enum.IsDefined(note.NoteType))
            { Issue("INVALID_NOTE_TYPE", "Adjustment note type is not recognised."); continue; }
            if (note.NoteDate.Date < fromDate.Date || note.NoteDate.Date > toDate.Date)
            { Issue("NOTE_OUTSIDE_PERIOD", "Note date is outside the selected return period."); continue; }
            if (note.OriginalInvoiceDate.Date > note.NoteDate.Date)
            { Issue("INVALID_ORIGINAL_INVOICE_DATE", "Original invoice date cannot be later than the adjustment note date."); continue; }
            var key = $"{note.NoteType}|{note.NoteNumber.Trim()}|{note.NoteDate:yyyyMMdd}";
            if (!seenNotes.Add(key))
            { Issue("DUPLICATE_ADJUSTMENT_NOTE", "Duplicate adjustment note number, date and type."); continue; }
            if (!ValidateCategory(note.Category, note.RecipientGstin, note.PlaceOfSupplyCode, out var categoryError))
            { Issue(categoryError.Code, categoryError.Message); continue; }
            if (!ValidateAmounts(note.TaxRate, note.TaxableValue, note.Igst, note.Cgst, note.Sgst, note.Cess, out var amountError))
            { Issue(amountError.Code, amountError.Message); continue; }
            acceptedNotes.Add(note);
        }

        var summaries = accepted.GroupBy(x => new { x.Category, Pos = (x.PlaceOfSupplyCode ?? string.Empty).Trim(), x.TaxRate })
            .OrderBy(x => x.Key.Category).ThenBy(x => x.Key.Pos).ThenBy(x => x.Key.TaxRate)
            .Select(g => new Gstr1TaxSummary(g.Key.Category, g.Key.Pos, g.Key.TaxRate, g.Count(),
                decimal.Round(g.Sum(x => x.TaxableValue), 2), decimal.Round(g.Sum(x => x.Igst), 2),
                decimal.Round(g.Sum(x => x.Cgst), 2), decimal.Round(g.Sum(x => x.Sgst), 2), decimal.Round(g.Sum(x => x.Cess), 2)))
            .ToList();
        var noteSummaries = acceptedNotes.GroupBy(x => new
            { x.NoteType, x.Category, Pos = (x.PlaceOfSupplyCode ?? string.Empty).Trim(), x.TaxRate })
            .OrderBy(x => x.Key.NoteType).ThenBy(x => x.Key.Category).ThenBy(x => x.Key.Pos).ThenBy(x => x.Key.TaxRate)
            .Select(g => new Gstr1NoteSummary(g.Key.NoteType, g.Key.Category, g.Key.Pos, g.Key.TaxRate,
                g.Count(), decimal.Round(g.Sum(x => x.TaxableValue), 2), decimal.Round(g.Sum(x => x.Igst), 2),
                decimal.Round(g.Sum(x => x.Cgst), 2), decimal.Round(g.Sum(x => x.Sgst), 2), decimal.Round(g.Sum(x => x.Cess), 2)))
            .ToList();
        return new Gstr1Workpaper
        {
            FromDate = fromDate.Date, ToDate = toDate.Date, SourceRowCount = rows.Count,
            IncludedRowCount = accepted.Count, NoteSourceCount = notes.Count, IncludedNoteCount = acceptedNotes.Count,
            Summaries = summaries, NoteSummaries = noteSummaries, Issues = issues
        };
    }

    private static bool ValidateCategory(Gstr1SupplyCategory category, string? gstin, string? pos, out (string Code, string Message) error)
    {
        if (!Enum.IsDefined(category))
        { error = ("INVALID_SUPPLY_CATEGORY", "Supply category is not recognised."); return false; }
        if (category is Gstr1SupplyCategory.B2B or Gstr1SupplyCategory.B2CL or Gstr1SupplyCategory.B2CS or Gstr1SupplyCategory.DeemedExport)
        {
            if (string.IsNullOrWhiteSpace(pos) || pos.Length != 2 || !pos.All(char.IsDigit))
            { error = ("INVALID_POS", "A two-digit place-of-supply state code is required."); return false; }
        }
        if (category == Gstr1SupplyCategory.B2B && (string.IsNullOrWhiteSpace(gstin) || !GstinPattern.IsMatch(gstin.Trim())))
        { error = ("INVALID_RECIPIENT_GSTIN", "B2B supply requires a structurally valid recipient GSTIN."); return false; }
        if (!string.IsNullOrWhiteSpace(gstin) && !GstinPattern.IsMatch(gstin.Trim()))
        { error = ("INVALID_RECIPIENT_GSTIN", "Recipient GSTIN format is invalid."); return false; }
        error = default;
        return true;
    }

    private static bool ValidateAmounts(decimal rate, decimal taxable, decimal igst, decimal cgst,
        decimal sgst, decimal cess, out (string Code, string Message) error)
    {
        if (rate < 0 || rate > 100 || decimal.Round(rate, 2) != rate)
        { error = ("INVALID_TAX_RATE", "Tax rate must be between 0 and 100 with at most two decimal places."); return false; }
        if (taxable < 0 || igst < 0 || cgst < 0 || sgst < 0 || cess < 0)
        { error = ("NEGATIVE_AMOUNT", "Negative values are not accepted; enter note amounts as positive magnitudes and select credit/debit direction."); return false; }
        var calculated = decimal.Round(taxable * rate / 100m, 2, MidpointRounding.AwayFromZero);
        var reported = igst + cgst + sgst;
        if (Math.Abs(calculated - reported) > RoundingTolerance)
        { error = ("TAX_AMOUNT_MISMATCH", $"Reported GST ({reported:N2}) differs from rate-derived GST ({calculated:N2}) by more than ₹{RoundingTolerance:N2}; verify cess and rounding."); return false; }
        if (igst > 0 && (cgst > 0 || sgst > 0))
        { error = ("MIXED_TAX_HEADS", "IGST and CGST/SGST are both populated; verify place-of-supply treatment."); return false; }
        if (cgst > 0 && Math.Abs(cgst - sgst) > RoundingTolerance)
        { error = ("INTRA_STATE_SPLIT", "CGST and SGST differ by more than the permitted workpaper tolerance."); return false; }
        error = default;
        return true;
    }
}
