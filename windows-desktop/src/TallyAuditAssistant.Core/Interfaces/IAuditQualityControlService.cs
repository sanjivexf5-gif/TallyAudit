using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TallyAuditAssistant.Core.Interfaces;

public class QualityControlCheckItem
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // Data Completeness, Audit Execution, Findings, Working Papers, Evidence, Review, Finalization
    public string Status { get; set; } = string.Empty; // Pass, Warning, Attention Required, Blocked, Not Applicable
    public string Explanation { get; set; } = string.Empty;
    public string Severity { get; set; } = "Low"; // Low, Medium, High, Critical
    public string RelatedEntityReference { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
}

public class AuditQualityControlSummary
{
    public string CompanyId { get; set; } = string.Empty;
    public string FinancialPeriodId { get; set; } = string.Empty;
    public string EngagementStatus { get; set; } = string.Empty;
    public int PassedChecksCount { get; set; }
    public int WarningsCount { get; set; }
    public int AttentionRequiredCount { get; set; }
    public int BlockedCount { get; set; }
    public bool IsReadyForReview { get; set; }
    public bool IsReadyForFinalization { get; set; }
    public List<QualityControlCheckItem> Checks { get; set; } = new();
}

public interface IAuditQualityControlService
{
    Task<AuditQualityControlSummary> GetQualityControlSummaryAsync(string companyId, string financialPeriodId, CancellationToken cancellationToken = default);
}
