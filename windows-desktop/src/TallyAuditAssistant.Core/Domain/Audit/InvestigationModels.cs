using System;
using System.Collections.Generic;
using TallyAuditAssistant.Core.Domain.Ledgers;
using TallyAuditAssistant.Core.Domain.Vouchers;

namespace TallyAuditAssistant.Core.Domain.Audit;

/// <summary>
/// Explicit lifecycle states for an audit exception investigation.
/// </summary>
public enum InvestigationStatus
{
    Open = 0,
    Investigating = 1,
    AwaitingEvidence = 2,
    AwaitingManagementResponse = 3,
    Resolved = 4,
    NotResolved = 5,
    Accepted = 6,
    Escalated = 7
}

/// <summary>
/// Auditor classification of exception root cause.
/// Note: Root cause is an auditor classification. The application must not automatically conclude
/// that a transaction is fraudulent, illegal, intentional, negligent, or caused by management.
/// </summary>
public enum RootCauseClassification
{
    DataEntry = 0,
    MasterDataIssue = 1,
    Configuration = 2,
    ProcessControlWeakness = 3,
    TimingCutoff = 4,
    TaxTreatment = 5,
    Duplicate = 6,
    ReconciliationDifference = 7,
    Other = 8,
    Unknown = 9
}

/// <summary>
/// Domain model for an investigation workspace attached to an audit exception.
/// </summary>
public class ExceptionInvestigation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ExceptionId { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string? FinancialPeriodId { get; set; }
    public string? AuditRunId { get; set; }
    public InvestigationStatus Status { get; set; } = InvestigationStatus.Open;
    public RootCauseClassification RootCause { get; set; } = RootCauseClassification.Unknown;
    public string? AuditorNotes { get; set; }
    public string? ManagementResponse { get; set; }
    public string? ProposedCorrectiveAction { get; set; }
    public string? ReviewerNotes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime? ClosedAt { get; set; }

    public List<InvestigationChecklistItem> ChecklistItems { get; set; } = new();
}

/// <summary>
/// Individual checklist step within an investigation.
/// </summary>
public class InvestigationChecklistItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string InvestigationId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Contextual related data collected from the audit repository and Tally data.
/// </summary>
public class InvestigationRelatedData
{
    public Voucher? SourceVoucher { get; set; }
    public IReadOnlyList<Voucher> RelatedVouchers { get; set; } = Array.Empty<Voucher>();
    public Ledger? Ledger { get; set; }
    public Ledger? Party { get; set; }
    public string? GstDetails { get; set; }
    public string? TdsDetails { get; set; }
    public IReadOnlyList<string> ReconciliationResults { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> DuplicateCandidates { get; set; } = Array.Empty<string>();
    public IReadOnlyList<WorkingPaperSummary> WorkingPapers { get; set; } = Array.Empty<WorkingPaperSummary>();
    public IReadOnlyList<AuditEvidenceSummary> Evidence { get; set; } = Array.Empty<AuditEvidenceSummary>();
    public IReadOnlyList<AuditTrailEntry> InvestigationAuditTrail { get; set; } = Array.Empty<AuditTrailEntry>();
}

public class WorkingPaperSummary
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string AuditArea { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PreparedBy { get; set; } = string.Empty;
}

public class AuditEvidenceSummary
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string EvidenceType { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? FileHash { get; set; }
}
