namespace TallyAuditAssistant.Engine.Gst;

/// <summary>Explicitly classified GSTR-3B liability source. Classification must be reviewed upstream.</summary>
public sealed record Gstr3BLiabilityLine(
    string SourceId, DateTime DocumentDate, Gstr3BLiabilitySection Section,
    decimal TaxableValue, decimal Igst, decimal Cgst, decimal Sgst, decimal Cess);

public enum Gstr3BLiabilitySection
{
    OutwardTaxable, ZeroRated, NilRatedExempt, ReverseCharge, NonGst,
    EcommerceSection311
}

/// <summary>ITC made available for reporting in Table 4(A), classified by source bucket.</summary>
public sealed record Gstr3BItcLine(
    string SourceId, DateTime DocumentDate, Gstr3BItcCategory Category,
    decimal Igst, decimal Cgst, decimal Sgst, decimal Cess);

public enum Gstr3BItcCategory { ImportGoods, ImportServices, ReverseCharge, Isd, OtherEligible }

/// <summary>Explicit ITC reversal classified to Table 4(B)(1) or 4(B)(2).</summary>
public sealed record Gstr3BItcReversal(
    string SourceId, DateTime DocumentDate, Gstr3BReversalCategory Category,
    decimal Igst, decimal Cgst, decimal Sgst, decimal Cess);

public enum Gstr3BReversalCategory { Rule42Or43OrOtherPermanent, OtherReversal }

public sealed record Gstr3BTaxHeadSummary(decimal Igst, decimal Cgst, decimal Sgst, decimal Cess)
{
    public decimal Total => Igst + Cgst + Sgst + Cess;
    public static Gstr3BTaxHeadSummary operator +(Gstr3BTaxHeadSummary a, Gstr3BTaxHeadSummary b) =>
        new(a.Igst + b.Igst, a.Cgst + b.Cgst, a.Sgst + b.Sgst, a.Cess + b.Cess);
}

public sealed record Gstr3BLiabilitySummary(
    Gstr3BLiabilitySection Section, int DocumentCount, decimal TaxableValue, Gstr3BTaxHeadSummary Tax);

public sealed record Gstr3BItcSummary(
    Gstr3BItcCategory Category, int DocumentCount, Gstr3BTaxHeadSummary Tax);

public sealed record Gstr3BReversalSummary(
    Gstr3BReversalCategory Category, int DocumentCount, Gstr3BTaxHeadSummary Tax);

public sealed record Gstr3BValidationIssue(string SourceId, string Code, string Message);

public sealed class Gstr3BWorkpaper
{
    public DateTime FromDate { get; init; }
    public DateTime ToDate { get; init; }
    public int LiabilitySourceCount { get; init; }
    public int ItcSourceCount { get; init; }
    public int ReversalSourceCount { get; init; }
    public int IncludedLiabilityCount { get; init; }
    public int IncludedItcCount { get; init; }
    public int IncludedReversalCount { get; init; }
    public IReadOnlyList<Gstr3BLiabilitySummary> Liabilities { get; init; } = Array.Empty<Gstr3BLiabilitySummary>();
    public IReadOnlyList<Gstr3BItcSummary> ItcAvailable { get; init; } = Array.Empty<Gstr3BItcSummary>();
    public IReadOnlyList<Gstr3BReversalSummary> ItcReversals { get; init; } = Array.Empty<Gstr3BReversalSummary>();
    public IReadOnlyList<Gstr3BValidationIssue> Issues { get; init; } = Array.Empty<Gstr3BValidationIssue>();
    public Gstr3BTaxHeadSummary TotalTaxLiability => Liabilities.Aggregate(
        new Gstr3BTaxHeadSummary(0, 0, 0, 0), (sum, row) => sum + row.Tax);
    public Gstr3BTaxHeadSummary TotalItcAvailable => ItcAvailable.Aggregate(
        new Gstr3BTaxHeadSummary(0, 0, 0, 0), (sum, row) => sum + row.Tax);
    public Gstr3BTaxHeadSummary TotalItcReversed => ItcReversals.Aggregate(
        new Gstr3BTaxHeadSummary(0, 0, 0, 0), (sum, row) => sum + row.Tax);
    public Gstr3BTaxHeadSummary NetItcAfterReversals => new(
        TotalItcAvailable.Igst - TotalItcReversed.Igst,
        TotalItcAvailable.Cgst - TotalItcReversed.Cgst,
        TotalItcAvailable.Sgst - TotalItcReversed.Sgst,
        TotalItcAvailable.Cess - TotalItcReversed.Cess);
    public bool IsReadyForReview => Issues.Count == 0 &&
        LiabilitySourceCount == IncludedLiabilityCount &&
        ItcSourceCount == IncludedItcCount &&
        ReversalSourceCount == IncludedReversalCount;
}
