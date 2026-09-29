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
/// Final auditor conclusion choices for investigation workspace.
/// </summary>
public enum InvestigationConclusion
{
    Pending = 0,
    NoExceptionNoted = 1,
    ExceptionConfirmed = 2,
    FurtherReviewRequired = 3,
    UnableToComplete = 4,
    NotApplicable = 5
}

/// <summary>
/// Recurrence classification based on comparison across audit runs and financial periods.
/// </summary>
public enum RecurrenceClassification
{
    New = 0,
    Recurring = 1,
    PreviouslyIdentified = 2,
    PreviouslyUnresolved = 3,
    ResolvedAndReappeared = 4
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
    public InvestigationConclusion Conclusion { get; set; } = InvestigationConclusion.Pending;
    public string? ConclusionNotes { get; set; }
    public RecurrenceClassification RecurrenceStatus { get; set; } = RecurrenceClassification.New;
    public string? RecurrenceSource { get; set; }
    public string? AuditorNotes { get; set; }
    public string? ManagementResponse { get; set; }
    public string? ProposedCorrectiveAction { get; set; }
    public string? ReviewerNotes { get; set; }
    public string? LinkedEvidenceIds { get; set; }
    public string? LinkedWorkingPaperIds { get; set; }
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
/// Summary of a potentially related exception identified deterministically.
/// </summary>
public class RelatedExceptionSummary
{
    public string FindingId { get; set; } = string.Empty;
    public RuleCategory Category { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public ReviewStatus Status { get; set; }
    public SeverityLevel Priority { get; set; }
    public string RelationshipType { get; set; } = string.Empty;
    public string? VoucherNumber { get; set; }
    public DateTime? VoucherDate { get; set; }
    public string? LedgerName { get; set; }
}

/// <summary>
/// Deterministic root-cause grouping summary.
/// </summary>
public class RootCauseGroupSummary
{
    public string GroupKey { get; set; } = string.Empty;
    public string GroupingType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int FindingCount { get; set; }
    public decimal TotalGrossAmount { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public int AffectedVouchersCount { get; set; }
    public int AffectedPartiesLedgersCount { get; set; }
    public string Headline { get; set; } = "Pattern requiring auditor review";
}

/// <summary>
/// Quantitative impact analysis calculated strictly with decimal precision.
/// </summary>
public class InvestigationImpactAnalysis
{
    public decimal GrossAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public int AffectedVouchersCount { get; set; }
    public int AffectedPartiesCount { get; set; }
    public int AffectedLedgersCount { get; set; }
    public decimal? PopulationTotalAmount { get; set; }
    public int? PopulationVoucherCount { get; set; }
    public decimal? PopulationPercentageByAmount { get; set; }
    public decimal? PopulationPercentageByCount { get; set; }
    public decimal? MaterialityThreshold { get; set; }
    public bool? IsMaterial { get; set; }
    public bool PopulationDataAvailable { get; set; }
    public string BasisDescription { get; set; } = "Not available";
}

/// <summary>
/// Event in the chronological investigation timeline.
/// </summary>
public class InvestigationTimelineEvent
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
}

/// <summary>
/// Recurrence analysis result comparing across prior audit runs and financial periods.
/// </summary>
public class RecurrenceAnalysisResult
{
    public RecurrenceClassification Classification { get; set; } = RecurrenceClassification.New;
    public string? SourceAuditRunId { get; set; }
    public string? SourceFinancialPeriod { get; set; }
    public string? PriorFindingId { get; set; }
    public string Explanation { get; set; } = "No prior occurrence detected in available audit runs.";
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
    public IReadOnlyList<RelatedExceptionSummary> RelatedExceptions { get; set; } = Array.Empty<RelatedExceptionSummary>();
    public IReadOnlyList<RootCauseGroupSummary> RootCauseGroups { get; set; } = Array.Empty<RootCauseGroupSummary>();
    public InvestigationImpactAnalysis Impact { get; set; } = new();
    public RecurrenceAnalysisResult Recurrence { get; set; } = new();
    public IReadOnlyList<InvestigationTimelineEvent> TimelineEvents { get; set; } = Array.Empty<InvestigationTimelineEvent>();
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
