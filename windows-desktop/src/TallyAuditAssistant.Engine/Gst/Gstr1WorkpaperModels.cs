namespace TallyAuditAssistant.Engine.Gst;

/// <summary>
/// Explicitly classified outward-supply source row. Values must be supplied by a reviewed
/// source mapping; the workpaper does not infer the statutory supply category.
/// </summary>
public sealed record Gstr1OutwardSupply(
    string VoucherId, string InvoiceNumber, DateTime InvoiceDate, string? RecipientGstin,
    string? PlaceOfSupplyCode, Gstr1SupplyCategory Category, decimal TaxRate,
    decimal TaxableValue, decimal Igst, decimal Cgst, decimal Sgst, decimal Cess,
    bool IsCreditNote = false, decimal? InvoiceValue = null, string? SupplierStateCode = null);

/// <summary>
/// A separately identified debit/credit note. Amounts are entered as positive magnitudes;
/// the workpaper applies the note's sign when calculating net outward supplies.
/// OriginalInvoiceDate may precede the selected return period.
/// </summary>
public sealed record Gstr1AdjustmentNote(
    string VoucherId, string NoteNumber, DateTime NoteDate, Gstr1AdjustmentNoteType NoteType,
    string OriginalInvoiceNumber, DateTime OriginalInvoiceDate, string? RecipientGstin,
    string? PlaceOfSupplyCode, Gstr1SupplyCategory Category, decimal TaxRate,
    decimal TaxableValue, decimal Igst, decimal Cgst, decimal Sgst, decimal Cess);

public enum Gstr1AdjustmentNoteType { CreditNote, DebitNote }

public enum Gstr1SupplyCategory { B2B, B2CL, B2CS, Export, Sez, DeemedExport }

public sealed record Gstr1ValidationIssue(string? VoucherId, string? InvoiceNumber, string Code, string Message);

public sealed record Gstr1TaxSummary(
    Gstr1SupplyCategory Category, string? PlaceOfSupplyCode, decimal TaxRate, int DocumentCount,
    decimal TaxableValue, decimal Igst, decimal Cgst, decimal Sgst, decimal Cess)
{
    public decimal TotalTax => Igst + Cgst + Sgst + Cess;
}

public sealed record Gstr1NoteSummary(
    Gstr1AdjustmentNoteType NoteType, Gstr1SupplyCategory Category, string? PlaceOfSupplyCode,
    decimal TaxRate, int DocumentCount, decimal TaxableValue, decimal Igst,
    decimal Cgst, decimal Sgst, decimal Cess)
{
    public decimal Sign => NoteType == Gstr1AdjustmentNoteType.CreditNote ? -1m : 1m;
    public decimal SignedTaxableValue => TaxableValue * Sign;
    public decimal SignedIgst => Igst * Sign;
    public decimal SignedCgst => Cgst * Sign;
    public decimal SignedSgst => Sgst * Sign;
    public decimal SignedCess => Cess * Sign;
}

public sealed class Gstr1Workpaper
{
    public DateTime FromDate { get; init; }
    public DateTime ToDate { get; init; }
    public int SourceRowCount { get; init; }
    public int IncludedRowCount { get; init; }
    public int NoteSourceCount { get; init; }
    public int IncludedNoteCount { get; init; }
    public IReadOnlyList<Gstr1TaxSummary> Summaries { get; init; } = Array.Empty<Gstr1TaxSummary>();
    public IReadOnlyList<Gstr1NoteSummary> NoteSummaries { get; init; } = Array.Empty<Gstr1NoteSummary>();
    /// <summary>Accepted source notes with original-invoice references, retained for audit drill-down.</summary>
    public IReadOnlyList<Gstr1AdjustmentNote> AdjustmentNotes { get; init; } = Array.Empty<Gstr1AdjustmentNote>();
    public IReadOnlyList<Gstr1ValidationIssue> Issues { get; init; } = Array.Empty<Gstr1ValidationIssue>();
    public decimal TaxableValue => Summaries.Sum(x => x.TaxableValue) + NoteSummaries.Sum(x => x.SignedTaxableValue);
    public decimal TotalIgst => Summaries.Sum(x => x.Igst) + NoteSummaries.Sum(x => x.SignedIgst);
    public decimal TotalCgst => Summaries.Sum(x => x.Cgst) + NoteSummaries.Sum(x => x.SignedCgst);
    public decimal TotalSgst => Summaries.Sum(x => x.Sgst) + NoteSummaries.Sum(x => x.SignedSgst);
    public decimal TotalCess => Summaries.Sum(x => x.Cess) + NoteSummaries.Sum(x => x.SignedCess);
    public decimal TotalTax => TotalIgst + TotalCgst + TotalSgst + TotalCess;
    public bool IsReadyForReview => SourceRowCount == IncludedRowCount &&
        NoteSourceCount == IncludedNoteCount && Issues.Count == 0;
}
