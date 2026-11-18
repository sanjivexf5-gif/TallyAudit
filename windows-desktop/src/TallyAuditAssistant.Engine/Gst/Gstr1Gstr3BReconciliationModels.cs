namespace TallyAuditAssistant.Engine.Gst;

public sealed record Gstr1Gstr3BVariance(
    string TaxHead, decimal Gstr1Amount, decimal Gstr3BAmount, decimal Difference, bool ExceedsTolerance);

/// <summary>
/// Comparison of net GSTR-1 outward supplies with GSTR-3B outward taxable and zero-rated
/// liability buckets. Reverse charge, nil/exempt, non-GST and e-commerce-specific buckets
/// are intentionally not silently folded into this comparison.
/// </summary>
public sealed class Gstr1Gstr3BReconciliation
{
    public decimal Tolerance { get; init; }
    public IReadOnlyList<Gstr1Gstr3BVariance> Variances { get; init; } = Array.Empty<Gstr1Gstr3BVariance>();
    public bool HasDifferences => Variances.Any(x => x.ExceedsTolerance);
    public bool IsReadyForReview => !HasDifferences;
}

