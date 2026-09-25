namespace TallyAuditAssistant.Core.Domain.Audit;

public class AuditRule
{
    public string RuleId { get; set; } = string.Empty;
    public RuleCategory Category { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SeverityLevel Severity { get; set; }
    public string SuggestedReview { get; set; } = string.Empty;
    public string? ParametersJson { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string Version { get; set; } = "1.0.0";
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}

public class AuditException
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CompanyId { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public RuleCategory Category { get; set; }
    public SeverityLevel Severity { get; set; }
    public string EntityId { get; set; } = string.Empty; // VoucherId or LedgerId
    public string EntityType { get; set; } = "Voucher";
    public string? VoucherNumber { get; set; }
    public DateTime? VoucherDate { get; set; }
    public string? LedgerName { get; set; }
    public decimal? FlaggedAmount { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public string? SuggestedCorrection { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    public string? AuditorNote { get; set; }
    public string? AuditorAssignedTo { get; set; }
    public DateTime FlaggedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}
