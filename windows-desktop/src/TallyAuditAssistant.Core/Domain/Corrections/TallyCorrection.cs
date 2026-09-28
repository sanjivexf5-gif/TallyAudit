using System;

namespace TallyAuditAssistant.Core.Domain.Corrections;

public enum CorrectionStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Applying = 3,
    Applied = 4,
    VerificationPending = 5,
    Verified = 6,
    Failed = 7,
    Rejected = 8,
    Cancelled = 9
}

public enum CorrectionType
{
    VoucherMasterField = 0,
    GstPartyGstin = 1,
    TdsPartyPan = 2,
    LedgerClassification = 3,
    VoucherNarration = 4
}

public class TallyCorrection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CompanyId { get; set; } = string.Empty;
    public string FinancialPeriodId { get; set; } = string.Empty;
    public string? AuditFindingId { get; set; }
    public string? VoucherId { get; set; }
    public string? VoucherNumber { get; set; }
    public string? LedgerId { get; set; }
    public string? LedgerName { get; set; }
    public CorrectionType CorrectionType { get; set; } = CorrectionType.VoucherMasterField;
    public string FieldName { get; set; } = string.Empty;
    public string? OriginalValue { get; set; }
    public string? ProposedValue { get; set; }
    public decimal? OriginalAmount { get; set; }
    public decimal? ProposedAmount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? EvidenceId { get; set; }
    public CorrectionStatus Status { get; set; } = CorrectionStatus.Draft;
    public string CreatedBy { get; set; } = "System";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? AppliedBy { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string? TallyResponse { get; set; }
    public string? TallyTransactionReference { get; set; }
    public string? VerificationStatus { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? FailureReason { get; set; }
    public string? BeforeSnapshot { get; set; }
    public string? AfterSnapshot { get; set; }
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsAiAssisted { get; set; }
}

public class TallyCorrectionAuditEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CorrectionId { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public record TallyWriteResult(
    bool IsSuccess,
    string StatusMessage,
    string? TallyResponseXml = null,
    string? TransactionReference = null,
    string? FailureCode = null,
    bool IsSupported = true
);

public record TallyVerificationResult(
    bool IsVerified,
    string VerificationMessage,
    string? ActualTallyValue = null,
    DateTime VerifiedAt = default
);
