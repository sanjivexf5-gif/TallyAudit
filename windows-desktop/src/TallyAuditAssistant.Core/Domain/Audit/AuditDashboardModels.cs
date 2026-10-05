using System;
using System.Collections.Generic;

namespace TallyAuditAssistant.Core.Domain.Audit;

public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public class AuditAreaSummary
{
    public string AreaName { get; set; } = string.Empty;
    public int FindingCount { get; set; }
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public int MediumCount { get; set; }
    public int LowCount { get; set; }
    public decimal ExceptionAmount { get; set; }
    public int RiskScore { get; set; }
}

public class TopFindingItem
{
    public string FindingId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public SeverityLevel Severity { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty;
    public string RecommendedAuditProcedure { get; set; } = string.Empty;
    public ReviewStatus ReviewStatus { get; set; }
    public int RiskScore { get; set; }
    public string? LinkedWorkingPaperId { get; set; }
    public bool HasWorkingPaper => !string.IsNullOrWhiteSpace(LinkedWorkingPaperId);
}

public class RiskScoringOptions
{
    public int CriticalWeight { get; set; } = 10;
    public int HighWeight { get; set; } = 6;
    public int MediumWeight { get; set; } = 3;
    public int LowWeight { get; set; } = 1;
    public int LowMaxThreshold { get; set; } = 24;
    public int MediumMaxThreshold { get; set; } = 49;
    public int HighMaxThreshold { get; set; } = 74;
    public int BenchmarkMaxPoints { get; set; } = 100;
}

public class AuditDashboardSummary
{
    public int OverallRiskScore { get; set; }
    public string OverallRiskLevel { get; set; } = "Low";
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public int MediumCount { get; set; }
    public int LowCount { get; set; }
    public int InformationalCount { get; set; }
    public int TotalFindings { get; set; }
    public int OpenFindings { get; set; }
    public int ReviewedFindings { get; set; }
    public int ResolvedFindings { get; set; }
    
    public decimal TotalExceptionAmount { get; set; }
    public decimal GstExceptionAmount { get; set; }
    public decimal TdsExceptionAmount { get; set; }
    public decimal DuplicateExceptionAmount { get; set; }
    public decimal JournalExceptionAmount { get; set; }
    public decimal SalesExceptionAmount { get; set; }
    public decimal PurchaseExceptionAmount { get; set; }
    public decimal ExpenseExceptionAmount { get; set; }

    public IReadOnlyList<AuditAreaSummary> AuditAreaSummaries { get; set; } = Array.Empty<AuditAreaSummary>();
    public IReadOnlyList<TopFindingItem> TopFindings { get; set; } = Array.Empty<TopFindingItem>();

    public bool HasExecutedAudit { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string FinancialYear { get; set; } = string.Empty;
    public DateTime? AuditPeriodFrom { get; set; }
    public DateTime? AuditPeriodTo { get; set; }
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
}
