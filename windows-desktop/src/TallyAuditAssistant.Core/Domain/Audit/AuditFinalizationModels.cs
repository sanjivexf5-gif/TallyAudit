using System;

namespace TallyAuditAssistant.Core.Domain.Audit;

public enum AuditLifecycleStatus
{
    Draft = 0,
    InProgress = 1,
    ReadyForReview = 2,
    UnderReview = 3,
    Returned = 4,
    ReadyForFinalization = 5,
    Finalized = 6,
    Archived = 7
}

public class AuditFinalizationState
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CompanyId { get; set; } = string.Empty;
    public string FinancialPeriodId { get; set; } = string.Empty;
    public AuditLifecycleStatus Status { get; set; } = AuditLifecycleStatus.Draft;
    
    // Completion percentage calculated from actual criteria
    public double CompletionPercentage { get; set; }
    
    // Auditor Conclusion
    public string AuditorConclusionStatus { get; set; } = "No Exceptions Noted"; // No Exceptions Noted, Exceptions Noted, Further Review Required, Unable to Complete, Not Applicable
    public string AuditorConclusionText { get; set; } = string.Empty;
    public string AuditorConclusionBasis { get; set; } = string.Empty;
    public DateTime? AuditorConclusionDate { get; set; }
    public string AuditorConclusionPreparedBy { get; set; } = string.Empty;
    
    // Reviewer Information
    public string? ReviewerName { get; set; }
    public string? ReviewerComments { get; set; }
    public DateTime? ReviewedAt { get; set; }
    
    // Finalization Details
    public string? FinalizedBy { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public bool IsReadOnly => Status == AuditLifecycleStatus.Finalized || Status == AuditLifecycleStatus.Archived;
}

public class ChecklistItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AuditId { get; set; } = string.Empty; // references AuditFinalizationState.Id or combination of company/period
    public string Section { get; set; } = string.Empty; // Planning, Execution, Findings, Evidence, Corrections, Final Review
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string CompletedBy { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty;
}

public class OpenItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AuditId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // Finding, Procedure, Evidence, Correction, etc.
    public string Priority { get; set; } = "Medium";
    public string Owner { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = "Open"; // Open, In Progress, Resolved
    public string? RelatedFindingId { get; set; }
    public string? RelatedProcedureId { get; set; }
    public string? RelatedEvidenceId { get; set; }
    public string Remarks { get; set; } = string.Empty;
}

public class ReviewNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AuditId { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty; // Finding/Procedure/Evidence reference
    public string Reviewer { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Comment { get; set; } = string.Empty;
    public string Status { get; set; } = "Open"; // Open, Responded, Resolved, Accepted, Rejected
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string Resolution { get; set; } = string.Empty;
}

public class AuditAmendment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AuditId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTime? ApprovedAt { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected
    public string Description { get; set; } = string.Empty;
}
