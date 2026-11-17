namespace TallyAuditAssistant.Engine.Gst;

/// <summary>
/// Explicitly classified outward-supply source row. Values must be supplied by a reviewed
/// source mapping; the workpaper does not infer the statutory supply category.
/// </summary>
public sealed record Gstr1OutwardSupply(
    string VoucherId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    string? RecipientGstin,
    string? PlaceOfSupplyCode,
    Gstr1SupplyCategory Category,
    decimal TaxRate,
    decimal TaxableValue,
    decimal Igst,
    decimal Cgst,
    decimal Sgst,
    decimal Cess,
    bool IsCreditNote = false);

public enum Gstr1SupplyCategory
{
    B2B,
    B2CL,
    B2CS,
    Export,
    Sez,
    DeemedExport
}

public sealed record Gstr1ValidationIssue(
    string? VoucherId,
    string? InvoiceNumber,
    string Code,
    string Message);

public sealed record Gstr1TaxSummary(
    Gstr1SupplyCategory Category,
    string? PlaceOfSupplyCode,
    decimal TaxRate,
    int DocumentCount,
    decimal TaxableValue,
    decimal Igst,
    decimal Cgst,
    decimal Sgst,
    decimal Cess)
{
    public decimal TotalTax => Igst + Cgst + Sgst + Cess;
}

public sealed class Gstr1Workpaper
{
    public DateTime FromDate { get; init; }
    public DateTime ToDate { get; init; }
    public int SourceRowCount { get; init; }
    public int IncludedRowCount { get; init; }
    public IReadOnlyList<Gstr1TaxSummary> Summaries { get; init; } = Array.Empty<Gstr1TaxSummary>();
    public IReadOnlyList<Gstr1ValidationIssue> Issues { get; init; } = Array.Empty<Gstr1ValidationIssue>();
    public decimal TaxableValue => Summaries.Sum(x => x.TaxableValue);
    public decimal TotalIgst => Summaries.Sum(x => x.Igst);
    public decimal TotalCgst => Summaries.Sum(x => x.Cgst);
    public decimal TotalSgst => Summaries.Sum(x => x.Sgst);
    public decimal TotalCess => Summaries.Sum(x => x.Cess);
    public decimal TotalTax => TotalIgst + TotalCgst + TotalSgst + TotalCess;
    public bool IsReadyForReview => SourceRowCount == IncludedRowCount && Issues.Count == 0;
}
