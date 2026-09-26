namespace TallyAuditAssistant.Core.Domain.Audit;

public class AuditResult
{
    public string ResultId { get; set; } = Guid.NewGuid().ToString();
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public RuleCategory Category { get; set; } = RuleCategory.GeneralAccounting;
    public string? VoucherId { get; set; }
    public string? LedgerId { get; set; }
    public string? VoucherNumber { get; set; }
    public DateTime? VoucherDate { get; set; }
    public decimal? FlaggedAmount { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public SeverityLevel Severity { get; set; } = SeverityLevel.Medium;
    public string Explanation { get; set; } = string.Empty;
    public string Evidence { get; set; } = "{}";
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    public string? Reviewer { get; set; }
    public string? ReviewerNote { get; set; }
}

public class AuditRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CompanyId { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime EndTime { get; set; } = DateTime.UtcNow;
    public int TransactionsAnalysed { get; set; }
    public int FindingsGenerated { get; set; }
    public string Status { get; set; } = "Completed";
}
