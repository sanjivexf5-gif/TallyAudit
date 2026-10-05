using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Engine.Services;

public class AuditFinalizationService : IAuditFinalizationService
{
    private readonly IAuditFinalizationRepository _repository;
    private readonly IAuditTrailService _auditTrailService;

    public AuditFinalizationService(
        IAuditFinalizationRepository repository,
        IAuditTrailService auditTrailService)
    {
        _repository = repository;
        _auditTrailService = auditTrailService;
    }

    public async Task<AuditFinalizationState> GetOrCreateStateAsync(string companyId, string financialPeriodId, CancellationToken cancellationToken = default)
    {
        var state = await _repository.GetStateAsync(companyId, financialPeriodId, cancellationToken);
        if (state == null)
        {
            state = new AuditFinalizationState
            {
                CompanyId = companyId,
                FinancialPeriodId = financialPeriodId,
                Status = AuditLifecycleStatus.Draft,
                CompletionPercentage = 0.0
            };
            await _repository.SaveStateAsync(state, cancellationToken);
            await InitializeDefaultChecklistAsync(state.Id, cancellationToken);
            
            await _auditTrailService.RecordActivityAsync(
                actionType: "AuditCreated",
                module: "AUDIT_LIFECYCLE",
                description: $"Audit workflow initiated for company {companyId}, period {financialPeriodId}",
                companyName: companyId,
                financialYear: financialPeriodId,
                ct: cancellationToken
            );
        }
        return state;
    }

    public async Task UpdateStatusAsync(string id, AuditLifecycleStatus status, string user, string? comments = null, CancellationToken cancellationToken = default)
    {
        var state = await _repository.GetStateByIdAsync(id, cancellationToken);
        if (state == null)
        {
            throw new KeyNotFoundException($"Audit finalization state {id} not found.");
        }

        ValidateTransition(state.Status, status);

        var originalStatus = state.Status;
        state.Status = status;

        if (status == AuditLifecycleStatus.Finalized)
        {
            state.FinalizedBy = user;
            state.FinalizedAt = DateTime.UtcNow;
        }

        await _repository.SaveStateAsync(state, cancellationToken);

        await _auditTrailService.RecordActivityAsync(
            actionType: "AuditStatusUpdated",
            module: "AUDIT_LIFECYCLE",
            description: $"Audit workflow state transitioned from {originalStatus} to {status} by {user}. Note: {comments ?? "None"}",
            companyName: state.CompanyId,
            financialYear: state.FinancialPeriodId,
            entityType: "AuditFinalizationState",
            entityId: state.Id,
            previousState: originalStatus.ToString(),
            newState: status.ToString(),
            ct: cancellationToken
        );
    }

    public async Task SubmitForReviewAsync(string id, string auditor, string reviewer, CancellationToken cancellationToken = default)
    {
        var state = await _repository.GetStateByIdAsync(id, cancellationToken);
        if (state == null) throw new KeyNotFoundException();

        if (state.Status != AuditLifecycleStatus.InProgress && state.Status != AuditLifecycleStatus.Returned)
        {
            throw new InvalidOperationException($"Cannot submit for review from status {state.Status}. Must be InProgress or Returned.");
        }

        state.Status = AuditLifecycleStatus.ReadyForReview;
        state.ReviewerName = reviewer;
        await _repository.SaveStateAsync(state, cancellationToken);

        await _auditTrailService.RecordActivityAsync(
            actionType: "AuditSubmittedForReview",
            module: "AUDIT_LIFECYCLE",
            description: $"Audit submitted for review by {auditor} to {reviewer}.",
            companyName: state.CompanyId,
            financialYear: state.FinancialPeriodId,
            ct: cancellationToken
        );
    }

    public async Task ReturnWithNotesAsync(string id, string reviewer, string comment, CancellationToken cancellationToken = default)
    {
        var state = await _repository.GetStateByIdAsync(id, cancellationToken);
        if (state == null) throw new KeyNotFoundException();

        state.Status = AuditLifecycleStatus.Returned;
        state.ReviewerComments = comment;
        state.ReviewedAt = DateTime.UtcNow;
        await _repository.SaveStateAsync(state, cancellationToken);

        await _auditTrailService.RecordActivityAsync(
            actionType: "AuditReturned",
            module: "AUDIT_LIFECYCLE",
            description: $"Audit returned by reviewer {reviewer} with comments: {comment}",
            companyName: state.CompanyId,
            financialYear: state.FinancialPeriodId,
            ct: cancellationToken
        );
    }

    public async Task ApproveForFinalizationAsync(string id, string reviewer, string comment, CancellationToken cancellationToken = default)
    {
        var state = await _repository.GetStateByIdAsync(id, cancellationToken);
        if (state == null) throw new KeyNotFoundException();

        state.Status = AuditLifecycleStatus.ReadyForFinalization;
        state.ReviewerComments = comment;
        state.ReviewedAt = DateTime.UtcNow;
        await _repository.SaveStateAsync(state, cancellationToken);

        await _auditTrailService.RecordActivityAsync(
            actionType: "AuditApprovedByReviewer",
            module: "AUDIT_LIFECYCLE",
            description: $"Audit approved for finalization by reviewer {reviewer}.",
            companyName: state.CompanyId,
            financialYear: state.FinancialPeriodId,
            ct: cancellationToken
        );
    }

    public async Task FinalizeAuditAsync(string id, string user, CancellationToken cancellationToken = default)
    {
        var state = await _repository.GetStateByIdAsync(id, cancellationToken);
        if (state == null) throw new KeyNotFoundException();

        // 1. Mandatory Checks
        var checklist = await _repository.GetChecklistAsync(id, cancellationToken);
        bool allCompleted = checklist.All(item => item.IsCompleted);
        if (!allCompleted)
        {
            throw new InvalidOperationException("Cannot finalize audit file: mandatory checklist items or procedures are incomplete.");
        }

        if (string.IsNullOrWhiteSpace(state.AuditorConclusionText))
        {
            throw new InvalidOperationException("Cannot finalize audit file: Auditor Conclusion is mandatory.");
        }

        state.Status = AuditLifecycleStatus.Finalized;
        state.FinalizedBy = user;
        state.FinalizedAt = DateTime.UtcNow;
        await _repository.SaveStateAsync(state, cancellationToken);

        await _auditTrailService.RecordActivityAsync(
            actionType: "AuditFinalized",
            module: "AUDIT_LIFECYCLE",
            description: $"Audit file permanently finalized as Read-Only by {user}.",
            companyName: state.CompanyId,
            financialYear: state.FinancialPeriodId,
            ct: cancellationToken
        );
    }

    public async Task ReopenAuditAsync(string id, string user, string reason, CancellationToken cancellationToken = default)
    {
        var state = await _repository.GetStateByIdAsync(id, cancellationToken);
        if (state == null) throw new KeyNotFoundException();

        if (state.Status != AuditLifecycleStatus.Finalized)
        {
            throw new InvalidOperationException("Only finalized audits can be reopened.");
        }

        state.Status = AuditLifecycleStatus.InProgress;
        await _repository.SaveStateAsync(state, cancellationToken);

        var amendment = new AuditAmendment
        {
            AuditId = id,
            Reason = reason,
            RequestedBy = user,
            RequestedAt = DateTime.UtcNow,
            ApprovedBy = user,
            ApprovedAt = DateTime.UtcNow,
            Status = "Approved",
            Description = $"Audit reopened for amendment by {user}."
        };
        await _repository.SaveAmendmentAsync(amendment, cancellationToken);

        await _auditTrailService.RecordActivityAsync(
            actionType: "AuditReopened",
            module: "AUDIT_LIFECYCLE",
            description: $"Audit reopened by {user} for amendment. Reason: {reason}",
            companyName: state.CompanyId,
            financialYear: state.FinancialPeriodId,
            ct: cancellationToken
        );
    }

    public async Task InitializeDefaultChecklistAsync(string auditId, CancellationToken cancellationToken = default)
    {
        var currentItems = await _repository.GetChecklistAsync(auditId, cancellationToken);
        if (currentItems.Count > 0) return;

        var defaults = new List<ChecklistItem>
        {
            new() { AuditId = auditId, Section = "Planning", Code = "PLAN-01", Description = "Audit plan exists and signed off" },
            new() { AuditId = auditId, Section = "Planning", Code = "RISK-01", Description = "Risk assessment and scope completed" },
            new() { AuditId = auditId, Section = "Planning", Code = "MAT-01", Description = "Audit materiality values documented" },
            new() { AuditId = auditId, Section = "Execution", Code = "PROC-01", Description = "Required audit procedures completed" },
            new() { AuditId = auditId, Section = "Execution", Code = "REC-01", Description = "All accounting/statutory reconciliations completed" },
            new() { AuditId = auditId, Section = "Execution", Code = "GST-01", Description = "Statutory GST Audit run executed successfully" },
            new() { AuditId = auditId, Section = "Execution", Code = "TDS-01", Description = "Statutory TDS Audit run executed successfully" },
            new() { AuditId = auditId, Section = "Findings", Code = "FIND-01", Description = "All identified exceptions and findings reviewed" },
            new() { AuditId = auditId, Section = "Evidence", Code = "EVI-01", Description = "Required evidentiary documents attached and valid" },
            new() { AuditId = auditId, Section = "Corrections", Code = "CORR-01", Description = "Proposed Tally Corrections reviewed and resolved" },
            new() { AuditId = auditId, Section = "Final Review", Code = "REV-01", Description = "Auditor professional conclusion documented" }
        };

        foreach (var item in defaults)
        {
            await _repository.SaveChecklistItemAsync(item, cancellationToken);
        }
    }

    public async Task SetChecklistItemCompletedAsync(string itemId, bool isCompleted, string user, string notes, CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetChecklistItemByIdAsync(itemId, cancellationToken);
        if (item == null) throw new KeyNotFoundException($"Checklist item {itemId} not found.");

        item.IsCompleted = isCompleted;
        item.CompletedAt = isCompleted ? DateTime.UtcNow : null;
        item.CompletedBy = isCompleted ? user : string.Empty;
        item.Notes = notes;

        await _repository.SaveChecklistItemAsync(item, cancellationToken);

        // Recalculate Completion Percentage
        var checklist = await _repository.GetChecklistAsync(item.AuditId, cancellationToken);
        var state = await _repository.GetStateByIdAsync(item.AuditId, cancellationToken);
        if (state != null)
        {
            int total = checklist.Count;
            int completed = checklist.Count(i => i.IsCompleted);
            state.CompletionPercentage = total > 0 ? Math.Round((double)completed / total * 100.0, 1) : 0.0;
            
            if (state.Status == AuditLifecycleStatus.Draft && completed > 0)
            {
                state.Status = AuditLifecycleStatus.InProgress;
            }

            await _repository.SaveStateAsync(state, cancellationToken);
        }
    }

    public async Task AddOpenItemAsync(OpenItem item, CancellationToken cancellationToken = default)
    {
        await _repository.SaveOpenItemAsync(item, cancellationToken);
    }

    public async Task AddReviewNoteAsync(ReviewNote note, CancellationToken cancellationToken = default)
    {
        await _repository.SaveReviewNoteAsync(note, cancellationToken);
    }

    private void ValidateTransition(AuditLifecycleStatus current, AuditLifecycleStatus target)
    {
        bool isValid = current switch
        {
            AuditLifecycleStatus.Draft => target == AuditLifecycleStatus.InProgress || target == AuditLifecycleStatus.ReadyForReview,
            AuditLifecycleStatus.InProgress => target == AuditLifecycleStatus.ReadyForReview || target == AuditLifecycleStatus.Draft,
            AuditLifecycleStatus.ReadyForReview => target == AuditLifecycleStatus.UnderReview || target == AuditLifecycleStatus.Returned,
            AuditLifecycleStatus.UnderReview => target == AuditLifecycleStatus.Returned || target == AuditLifecycleStatus.ReadyForFinalization,
            AuditLifecycleStatus.Returned => target == AuditLifecycleStatus.InProgress || target == AuditLifecycleStatus.ReadyForReview,
            AuditLifecycleStatus.ReadyForFinalization => target == AuditLifecycleStatus.Finalized,
            AuditLifecycleStatus.Finalized => target == AuditLifecycleStatus.Archived || target == AuditLifecycleStatus.InProgress, // InProgress represents a controlled reopening
            AuditLifecycleStatus.Archived => false,
            _ => false
        };

        if (!isValid)
        {
            throw new InvalidOperationException($"Invalid audit workflow transition from {current} to {target}.");
        }
    }
}
