namespace TallyAuditAssistant.Engine.Gst;

public enum Gstr1HsnSupplyTab { B2B, B2C }

/// <summary>Reviewed HSN/SAC summary line for GSTR-1 Table 12. It is not a tariff classification opinion.</summary>
public sealed record Gstr1HsnLine(
    string SourceId, Gstr1HsnSupplyTab SupplyTab, string HsnCode, string Description,
    string Uqc, decimal Quantity, decimal TaxRate, decimal TaxableValue,
    decimal Igst, decimal Cgst, decimal Sgst, decimal Cess);

public sealed record Gstr1HsnIssue(string? SourceId, string Code, string Message);

public sealed record Gstr1HsnSummary(
    Gstr1HsnSupplyTab SupplyTab, string HsnCode, string Description, string Uqc,
    decimal TaxRate, int SourceLineCount, decimal Quantity, decimal TaxableValue,
    decimal Igst, decimal Cgst, decimal Sgst, decimal Cess)
{
    public decimal TotalTax => Igst + Cgst + Sgst + Cess;
}

public sealed class Gstr1HsnWorkpaper
{
    public DateTime ReturnPeriodStart { get; init; }
    public DateTime ReturnPeriodEnd { get; init; }
    public decimal PreviousFinancialYearAggregateTurnover { get; init; }
    public int SourceLineCount { get; init; }
    public int IncludedLineCount { get; init; }
    public IReadOnlyList<Gstr1HsnSummary> Summaries { get; init; } = Array.Empty<Gstr1HsnSummary>();
    public IReadOnlyList<Gstr1HsnIssue> Issues { get; init; } = Array.Empty<Gstr1HsnIssue>();
    public bool IsReadyForReview => SourceLineCount == IncludedLineCount && Issues.Count == 0;
}
