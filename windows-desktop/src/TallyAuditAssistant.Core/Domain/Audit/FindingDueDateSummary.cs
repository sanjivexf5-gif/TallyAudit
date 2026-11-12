namespace TallyAuditAssistant.Core.Domain.Audit;

/// <summary>
/// Lightweight, database-aggregated counts for actionable finding review deadlines.
/// </summary>
public sealed class FindingDueDateSummary
{
    public int OverdueCount { get; set; }
    public int DueSoonCount { get; set; }
}
