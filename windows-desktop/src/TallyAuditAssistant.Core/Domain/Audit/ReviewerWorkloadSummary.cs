namespace TallyAuditAssistant.Core.Domain.Audit;

/// <summary>
/// Database-aggregated review workload for one assigned reviewer or the unassigned queue.
/// </summary>
public sealed class ReviewerWorkloadSummary
{
    public string Reviewer { get; set; } = string.Empty;
    public int TotalFindings { get; set; }
    public int PendingCount { get; set; }
    public int ClarificationCount { get; set; }
    public int OverdueCount { get; set; }
    public int DueSoonCount { get; set; }
    public int ReviewedCount { get; set; }
    public int ResolvedCount { get; set; }
}
