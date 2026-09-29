using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Ledgers;
using TallyAuditAssistant.Core.Domain.Vouchers;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Engine.Services;

public class InvestigationService : IInvestigationService
{
    private readonly IInvestigationRepository _investigationRepository;
    private readonly IAuditRepository _auditRepository;
    private readonly IAuditTrailService _auditTrailService;
    private readonly IAuditFinalizationRepository _finalizationRepository;
    private readonly ILogger<InvestigationService> _logger;

    public InvestigationService(
        IInvestigationRepository investigationRepository,
        IAuditRepository auditRepository,
        IAuditTrailService auditTrailService,
        IAuditFinalizationRepository finalizationRepository,
        ILogger<InvestigationService> logger)
    {
        _investigationRepository = investigationRepository;
        _auditRepository = auditRepository;
        _auditTrailService = auditTrailService;
        _finalizationRepository = finalizationRepository;
        _logger = logger;
    }

    public async Task<ExceptionInvestigation> GetOrCreateInvestigationAsync(
        string exceptionId,
        string companyId,
        string username,
        string? financialPeriodId = null,
        string? auditRunId = null,
        CancellationToken ct = default)
    {
        var existing = await _investigationRepository.GetByExceptionIdAsync(exceptionId, ct);
        if (existing != null)
        {
            return existing;
        }

        var investigation = new ExceptionInvestigation
        {
            Id = Guid.NewGuid().ToString(),
            ExceptionId = exceptionId,
            CompanyId = companyId,
            FinancialPeriodId = financialPeriodId,
            AuditRunId = auditRunId,
            Status = InvestigationStatus.Open,
            RootCause = RootCauseClassification.Unknown,
            CreatedBy = username,
            UpdatedBy = username,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ChecklistItems = CreateDefaultChecklist(Guid.NewGuid().ToString())
        };

        // Fix checklist foreign keys to match the new investigation ID
        foreach (var item in investigation.ChecklistItems)
        {
            item.InvestigationId = investigation.Id;
        }

        await _investigationRepository.SaveInvestigationAsync(investigation, ct);

        await _auditTrailService.RecordActivityAsync(
            action: "InvestigationCreated",
            category: "INVESTIGATION",
            description: $"Investigation opened for exception {exceptionId} by {username}",
            companyId: companyId,
            financialPeriodId: financialPeriodId,
            entityType: "ExceptionInvestigation",
            entityId: investigation.Id,
            metadataJson: $"{{\"exceptionId\":\"{exceptionId}\",\"status\":\"{investigation.Status}\"}}",
            ct: ct
        );

        return investigation;
    }

    public async Task<ExceptionInvestigation?> GetInvestigationByIdAsync(string investigationId, CancellationToken ct = default)
    {
        return await _investigationRepository.GetByIdAsync(investigationId, ct);
    }

    public async Task<ExceptionInvestigation?> GetInvestigationByExceptionIdAsync(string exceptionId, CancellationToken ct = default)
    {
        return await _investigationRepository.GetByExceptionIdAsync(exceptionId, ct);
    }

    public async Task<IReadOnlyList<ExceptionInvestigation>> GetInvestigationsForCompanyAsync(string companyId, InvestigationStatus? status = null, CancellationToken ct = default)
    {
        return await _investigationRepository.GetByCompanyIdAsync(companyId, status, ct);
    }

    public bool CanTransition(InvestigationStatus currentStatus, InvestigationStatus targetStatus)
    {
        if (currentStatus == targetStatus) return true;

        return currentStatus switch
        {
            InvestigationStatus.Open => targetStatus == InvestigationStatus.Investigating,

            InvestigationStatus.Investigating => targetStatus is InvestigationStatus.AwaitingEvidence
                                                              or InvestigationStatus.AwaitingManagementResponse
                                                              or InvestigationStatus.Resolved
                                                              or InvestigationStatus.NotResolved
                                                              or InvestigationStatus.Accepted
                                                              or InvestigationStatus.Escalated,

            InvestigationStatus.AwaitingEvidence => targetStatus is InvestigationStatus.Investigating
                                                                 or InvestigationStatus.Resolved
                                                                 or InvestigationStatus.Escalated
                                                                 or InvestigationStatus.AwaitingManagementResponse,

            InvestigationStatus.AwaitingManagementResponse => targetStatus is InvestigationStatus.Investigating
                                                                            or InvestigationStatus.Resolved
                                                                            or InvestigationStatus.Accepted
                                                                            or InvestigationStatus.Escalated,

            InvestigationStatus.NotResolved => targetStatus is InvestigationStatus.Investigating
                                                            or InvestigationStatus.Escalated
                                                            or InvestigationStatus.Accepted,

            InvestigationStatus.Accepted => targetStatus is InvestigationStatus.Investigating
                                                         or InvestigationStatus.Resolved,

            InvestigationStatus.Resolved => targetStatus == InvestigationStatus.Investigating,

            InvestigationStatus.Escalated => targetStatus is InvestigationStatus.Investigating
                                                          or InvestigationStatus.Resolved,

            _ => false
        };
    }

    public IReadOnlyList<InvestigationStatus> GetAllowedTransitions(InvestigationStatus currentStatus)
    {
        return Enum.GetValues<InvestigationStatus>()
            .Where(s => s != currentStatus && CanTransition(currentStatus, s))
            .ToList();
    }

    public async Task<bool> TransitionStatusAsync(
        string investigationId,
        InvestigationStatus newStatus,
        string username,
        string? reason = null,
        CancellationToken ct = default)
    {
        var investigation = await _investigationRepository.GetByIdAsync(investigationId, ct);
        if (investigation == null)
        {
            throw new KeyNotFoundException($"Investigation {investigationId} was not found.");
        }

        if (!CanTransition(investigation.Status, newStatus))
        {
            throw new InvalidOperationException($"Invalid status transition from {investigation.Status} to {newStatus}.");
        }

        DateTime? closedAt = newStatus switch
        {
            InvestigationStatus.Resolved or InvestigationStatus.NotResolved or InvestigationStatus.Accepted => DateTime.UtcNow,
            InvestigationStatus.Investigating or InvestigationStatus.Open => null,
            _ => investigation.ClosedAt
        };

        var oldStatus = investigation.Status;
        await _investigationRepository.UpdateStatusAsync(investigationId, newStatus, username, closedAt, ct);

        await _auditTrailService.RecordActivityAsync(
            action: "InvestigationStatusChanged",
            category: "INVESTIGATION",
            description: $"Investigation {investigationId} transitioned from {oldStatus} to {newStatus}. Reason: {reason ?? "Status updated by auditor"}",
            companyId: investigation.CompanyId,
            financialPeriodId: investigation.FinancialPeriodId,
            entityType: "ExceptionInvestigation",
            entityId: investigation.Id,
            metadataJson: $"{{\"from\":\"{oldStatus}\",\"to\":\"{newStatus}\",\"reason\":\"{reason}\"}}",
            ct: ct
        );

        // If investigation is resolved, update parent exception review status as well
        if (newStatus == InvestigationStatus.Resolved)
        {
            try
            {
                await _auditRepository.UpdateExceptionStatusAsync(investigation.ExceptionId, ReviewStatus.Resolved, $"Resolved via Investigation {investigationId}. {reason}", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update parent exception status for {ExceptionId}", investigation.ExceptionId);
            }
        }

        return true;
    }

    public async Task UpdateInvestigationAsync(ExceptionInvestigation investigation, string username, CancellationToken ct = default)
    {
        var existing = await _investigationRepository.GetByIdAsync(investigation.Id, ct);
        if (existing == null)
        {
            throw new KeyNotFoundException($"Investigation {investigation.Id} was not found.");
        }

        investigation.UpdatedBy = username;
        investigation.UpdatedAt = DateTime.UtcNow;

        await _investigationRepository.UpdateInvestigationAsync(investigation, ct);

        await _auditTrailService.RecordActivityAsync(
            action: "InvestigationUpdated",
            category: "INVESTIGATION",
            description: $"Investigation {investigation.Id} updated by {username}. Root cause: {investigation.RootCause}",
            companyId: investigation.CompanyId,
            financialPeriodId: investigation.FinancialPeriodId,
            entityType: "ExceptionInvestigation",
            entityId: investigation.Id,
            metadataJson: $"{{\"rootCause\":\"{investigation.RootCause}\",\"status\":\"{investigation.Status}\"}}",
            ct: ct
        );
    }

    public async Task ToggleChecklistItemAsync(
        string itemId,
        bool isCompleted,
        string username,
        string? notes = null,
        CancellationToken ct = default)
    {
        var item = new InvestigationChecklistItem
        {
            Id = itemId,
            IsCompleted = isCompleted,
            CompletedAt = isCompleted ? DateTime.UtcNow : null,
            CompletedBy = isCompleted ? username : null,
            Notes = notes
        };

        await _investigationRepository.UpdateChecklistItemAsync(item, ct);

        await _auditTrailService.RecordActivityAsync(
            action: "ChecklistItemUpdated",
            category: "INVESTIGATION",
            description: $"Checklist item {itemId} set to {(isCompleted ? "Completed" : "Incomplete")} by {username}",
            entityType: "InvestigationChecklistItem",
            entityId: itemId,
            ct: ct
        );
    }

    public async Task<InvestigationRelatedData> GetRelatedDataAsync(string exceptionId, string companyId, CancellationToken ct = default)
    {
        var relatedData = new InvestigationRelatedData();

        // 1. Fetch Exception from audit repository
        var exceptions = await _auditRepository.GetExceptionsAsync(companyId, take: 500, cancellationToken: ct);
        var targetException = exceptions?.FirstOrDefault(e => e.Id == exceptionId);

        // 2. Fetch Audit Trail entries for this exception/investigation
        var auditTrail = await _auditTrailService.GetAuditTrailAsync(companyId, limit: 100, ct: ct);
        if (auditTrail != null)
        {
            relatedData.InvestigationAuditTrail = auditTrail
                .Where(a => a != null && (a.EntityId == exceptionId || (a.Description != null && a.Description.Contains(exceptionId))))
                .OrderByDescending(a => a.Timestamp)
                .ToList();
        }

        // 3. Fetch Working Papers & Evidence from finalization/evidence repository
        try
        {
            var openItems = await _finalizationRepository.GetOpenItemsAsync(companyId, ct);
            var reviewNotes = await _finalizationRepository.GetReviewNotesAsync(companyId, ct);
            
            if (reviewNotes != null)
            {
                relatedData.WorkingPapers = reviewNotes.Select(r => new WorkingPaperSummary
                {
                    Id = r.Id,
                    Title = r.Reference,
                    AuditArea = r.Area,
                    Status = r.Status,
                    PreparedBy = r.Reviewer
                }).ToList();
            }

            if (openItems != null)
            {
                relatedData.Evidence = openItems.Select(o => new AuditEvidenceSummary
                {
                    Id = o.Id,
                    Description = o.Description,
                    EvidenceType = o.Category,
                    ReferenceNumber = o.RelatedFindingId,
                    Status = o.Status
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load working papers / evidence for company {CompanyId}", companyId);
        }

        // 4. If we have a target exception, gather contextual details
        if (targetException != null)
        {
            if (!string.IsNullOrEmpty(targetException.LedgerName))
            {
                relatedData.Ledger = new Ledger
                {
                    Name = targetException.LedgerName,
                    CompanyId = companyId,
                    ParentGroup = targetException.Category.ToString(),
                    ClosingBalance = targetException.FlaggedAmount ?? 0
                };
            }

            if (!string.IsNullOrEmpty(targetException.VoucherNumber))
            {
                relatedData.SourceVoucher = new Voucher
                {
                    CompanyId = companyId,
                    VoucherNumber = targetException.VoucherNumber,
                    VoucherDate = targetException.VoucherDate ?? DateTime.UtcNow,
                    TotalAmount = targetException.FlaggedAmount ?? 0,
                    PartyLedgerName = targetException.LedgerName,
                    Narration = targetException.SuggestedCorrection ?? "Audit exception flagged"
                };
            }

            if (targetException.Category == RuleCategory.GST)
            {
                relatedData.GstDetails = $"Rule: {targetException.RuleId} ({targetException.RuleName}). Evidence: {targetException.EvidenceJson}";
            }
            else if (targetException.Category == RuleCategory.TDS)
            {
                relatedData.TdsDetails = $"Rule: {targetException.RuleId} ({targetException.RuleName}). Evidence: {targetException.EvidenceJson}";
            }
            else if (targetException.Category == RuleCategory.DuplicateDetection)
            {
                relatedData.DuplicateCandidates = new List<string>
                {
                    $"Identical Voucher: {targetException.VoucherNumber} Amount: ₹{targetException.FlaggedAmount:N2}",
                    $"Matching Reference: {targetException.EntityId}"
                };
            }

            relatedData.ReconciliationResults = new List<string>
            {
                $"Analyzed under Audit Rule {targetException.RuleId} ({targetException.RuleName})",
                $"Severity: {targetException.Severity}, Status: {targetException.Status}"
            };
        }

        return relatedData;
    }

    private static List<InvestigationChecklistItem> CreateDefaultChecklist(string investigationId)
    {
        return new List<InvestigationChecklistItem>
        {
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-01", Description = "Review source transaction" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-02", Description = "Review related ledger" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-03", Description = "Review related party/customer/vendor" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-04", Description = "Review supporting evidence" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-05", Description = "Check related vouchers" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-06", Description = "Check related GST information" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-07", Description = "Check related TDS information" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-08", Description = "Check reconciliation results" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-09", Description = "Check duplicate candidates" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-10", Description = "Check period/cut-off" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-11", Description = "Check master data" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-12", Description = "Obtain additional evidence" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-13", Description = "Obtain management response" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-14", Description = "Perform re-check" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-15", Description = "Record conclusion" }
        };
    }
}
