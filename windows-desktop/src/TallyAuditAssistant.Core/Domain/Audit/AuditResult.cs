namespace TallyAuditAssistant.Core.Domain.Audit;

public class AuditResult
{
    public string ResultId { get; set; } = Guid.NewGuid().ToString();
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
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
