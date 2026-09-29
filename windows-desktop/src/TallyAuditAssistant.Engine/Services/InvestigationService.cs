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

    public async Task SaveConclusionAsync(
        string investigationId, 
        InvestigationConclusion conclusion, 
        string? notes, 
        string username, 
        CancellationToken ct = default)
    {
        var investigation = await _investigationRepository.GetByIdAsync(investigationId, ct);
        if (investigation == null)
        {
            throw new KeyNotFoundException($"Investigation {investigationId} was not found.");
        }

        var oldConclusion = investigation.Conclusion;
        investigation.Conclusion = conclusion;
        investigation.ConclusionNotes = notes;
        investigation.UpdatedBy = username;
        investigation.UpdatedAt = DateTime.UtcNow;

        await _investigationRepository.UpdateInvestigationAsync(investigation, ct);

        await _auditTrailService.RecordActivityAsync(
            action: "ConclusionChanged",
            category: "INVESTIGATION",
            description: $"Investigation {investigationId} conclusion changed from {oldConclusion} to {conclusion}. Notes: {notes ?? "None"}",
            companyId: investigation.CompanyId,
            financialPeriodId: investigation.FinancialPeriodId,
            entityType: "ExceptionInvestigation",
            entityId: investigation.Id,
            metadataJson: $"{{\"from\":\"{oldConclusion}\",\"to\":\"{conclusion}\",\"notes\":\"{notes}\"}}",
            ct: ct
        );
    }

    public async Task LinkEvidenceAsync(
        string investigationId, 
        string evidenceId, 
        string username, 
        CancellationToken ct = default)
    {
        var investigation = await _investigationRepository.GetByIdAsync(investigationId, ct);
        if (investigation == null)
        {
            throw new KeyNotFoundException($"Investigation {investigationId} was not found.");
        }

        var currentIds = (investigation.LinkedEvidenceIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (!currentIds.Contains(evidenceId))
        {
            currentIds.Add(evidenceId);
            investigation.LinkedEvidenceIds = string.Join(",", currentIds);
            investigation.UpdatedBy = username;
            investigation.UpdatedAt = DateTime.UtcNow;

            await _investigationRepository.UpdateInvestigationAsync(investigation, ct);

            await _auditTrailService.RecordActivityAsync(
                action: "EvidenceLinked",
                category: "INVESTIGATION",
                description: $"Audit evidence {evidenceId} linked to investigation {investigationId} by {username}",
                companyId: investigation.CompanyId,
                financialPeriodId: investigation.FinancialPeriodId,
                entityType: "ExceptionInvestigation",
                entityId: investigation.Id,
                metadataJson: $"{{\"evidenceId\":\"{evidenceId}\"}}",
                ct: ct
            );
        }
    }

    public async Task LinkWorkingPaperAsync(
        string investigationId, 
        string workingPaperId, 
        string username, 
        CancellationToken ct = default)
    {
        var investigation = await _investigationRepository.GetByIdAsync(investigationId, ct);
        if (investigation == null)
        {
            throw new KeyNotFoundException($"Investigation {investigationId} was not found.");
        }

        var currentIds = (investigation.LinkedWorkingPaperIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (!currentIds.Contains(workingPaperId))
        {
            currentIds.Add(workingPaperId);
            investigation.LinkedWorkingPaperIds = string.Join(",", currentIds);
            investigation.UpdatedBy = username;
            investigation.UpdatedAt = DateTime.UtcNow;

            await _investigationRepository.UpdateInvestigationAsync(investigation, ct);

            await _auditTrailService.RecordActivityAsync(
                action: "WorkingPaperLinked",
                category: "INVESTIGATION",
                description: $"Working paper {workingPaperId} linked to investigation {investigationId} by {username}",
                companyId: investigation.CompanyId,
                financialPeriodId: investigation.FinancialPeriodId,
                entityType: "ExceptionInvestigation",
                entityId: investigation.Id,
                metadataJson: $"{{\"workingPaperId\":\"{workingPaperId}\"}}",
                ct: ct
            );
        }
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
            action: "InvestigationChecklistUpdated",
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
            var filteredTrail = auditTrail
                .Where(a => a != null && (a.EntityId == exceptionId || (a.Description != null && a.Description.Contains(exceptionId))))
                .OrderByDescending(a => a.Timestamp)
                .ToList();

            relatedData.InvestigationAuditTrail = filteredTrail;

            relatedData.TimelineEvents = filteredTrail.Select(a => new InvestigationTimelineEvent
            {
                Timestamp = a.Timestamp,
                EventType = a.Action,
                Title = a.Action,
                Description = a.Description,
                Actor = a.Username ?? "Auditor",
                MetadataJson = a.MetadataJson
            }).ToList();
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

        // 4. If target exception exists, compute related exceptions, grouping, impact, and recurrence
        if (targetException != null && exceptions != null)
        {
            // Potentially related exceptions (same voucher, ledger, category, or rule)
            var relatedList = exceptions
                .Where(e => e.Id != targetException.Id && (
                    (!string.IsNullOrEmpty(e.VoucherNumber) && e.VoucherNumber == targetException.VoucherNumber) ||
                    (!string.IsNullOrEmpty(e.LedgerName) && e.LedgerName == targetException.LedgerName) ||
                    e.Category == targetException.Category ||
                    e.RuleId == targetException.RuleId
                ))
                .Select(e => new RelatedExceptionSummary
                {
                    FindingId = e.Id,
                    Category = e.Category,
                    RuleId = e.RuleId,
                    RuleName = e.RuleName,
                    GrossAmount = e.FlaggedAmount ?? 0m,
                    TaxAmount = (e.FlaggedAmount ?? 0m) * 0.18m, // Estimated tax component where applicable
                    Status = e.Status,
                    Priority = e.Severity,
                    RelationshipType = (!string.IsNullOrEmpty(e.VoucherNumber) && e.VoucherNumber == targetException.VoucherNumber) ? "Same Voucher" :
                                       (!string.IsNullOrEmpty(e.LedgerName) && e.LedgerName == targetException.LedgerName) ? "Same Ledger" :
                                       (e.RuleId == targetException.RuleId) ? "Same Rule" : "Same Category",
                    VoucherNumber = e.VoucherNumber,
                    VoucherDate = e.VoucherDate,
                    LedgerName = e.LedgerName
                })
                .Take(50)
                .ToList();

            relatedData.RelatedExceptions = relatedList;

            // Deterministic Root-Cause Grouping
            var groups = exceptions
                .GroupBy(e => string.IsNullOrEmpty(e.LedgerName) ? e.Category.ToString() : e.LedgerName)
                .Select(g => new RootCauseGroupSummary
                {
                    GroupKey = g.Key,
                    GroupingType = "Ledger / Category",
                    Title = $"Pattern: {g.Key}",
                    FindingCount = g.Count(),
                    TotalGrossAmount = g.Sum(x => x.FlaggedAmount ?? 0m),
                    TotalTaxAmount = g.Sum(x => (x.FlaggedAmount ?? 0m) * 0.18m),
                    AffectedVouchersCount = g.Where(x => !string.IsNullOrEmpty(x.VoucherNumber)).Select(x => x.VoucherNumber).Distinct().Count(),
                    AffectedPartiesLedgersCount = g.Where(x => !string.IsNullOrEmpty(x.LedgerName)).Select(x => x.LedgerName).Distinct().Count(),
                    Headline = "Pattern requiring auditor review"
                })
                .OrderByDescending(g => g.FindingCount)
                .Take(10)
                .ToList();

            relatedData.RootCauseGroups = groups;

            // Impact Analysis
            decimal gross = targetException.FlaggedAmount ?? 0m;
            decimal tax = gross * 0.18m;
            int affectedVouchers = string.IsNullOrEmpty(targetException.VoucherNumber) ? 1 : exceptions.Count(e => e.VoucherNumber == targetException.VoucherNumber);
            int affectedParties = string.IsNullOrEmpty(targetException.LedgerName) ? 1 : exceptions.Count(e => e.LedgerName == targetException.LedgerName);

            int totalVoucherCount = await _auditRepository.GetVoucherCountAsync(companyId, ct);
            
            relatedData.Impact = new InvestigationImpactAnalysis
            {
                GrossAmount = gross,
                TaxAmount = tax,
                AffectedVouchersCount = affectedVouchers,
                AffectedPartiesCount = affectedParties,
                AffectedLedgersCount = string.IsNullOrEmpty(targetException.LedgerName) ? 1 : 1,
                PopulationTotalAmount = null,
                PopulationVoucherCount = totalVoucherCount > 0 ? totalVoucherCount : null,
                PopulationPercentageByAmount = null,
                PopulationPercentageByCount = (totalVoucherCount > 0) ? Math.Round(((decimal)affectedVouchers / totalVoucherCount) * 100m, 2) : null,
                MaterialityThreshold = 100000m,
                IsMaterial = gross >= 100000m,
                PopulationDataAvailable = totalVoucherCount > 0,
                BasisDescription = totalVoucherCount > 0 ? $"Evaluated against {totalVoucherCount} synchronized vouchers." : "Not available"
            };

            // Recurrence Analysis
            var priorMatching = exceptions.Where(e => e.Id != targetException.Id && e.RuleId == targetException.RuleId && e.LedgerName == targetException.LedgerName).ToList();
            if (priorMatching.Any(p => p.Status == ReviewStatus.Resolved))
            {
                relatedData.Recurrence = new RecurrenceAnalysisResult
                {
                    Classification = RecurrenceClassification.ResolvedAndReappeared,
                    PriorFindingId = priorMatching.First(p => p.Status == ReviewStatus.Resolved).Id,
                    Explanation = "Identified previously resolved exception for identical rule and ledger."
                };
            }
            else if (priorMatching.Any())
            {
                relatedData.Recurrence = new RecurrenceAnalysisResult
                {
                    Classification = RecurrenceClassification.Recurring,
                    PriorFindingId = priorMatching.First().Id,
                    Explanation = "Recurring exception detected in current audit population."
                };
            }
            else
            {
                relatedData.Recurrence = new RecurrenceAnalysisResult
                {
                    Classification = RecurrenceClassification.New,
                    Explanation = "New finding. No prior occurrence detected for rule and ledger."
                };
            }

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
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-02", Description = "Review ledger" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-03", Description = "Review party details" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-04", Description = "Review supporting evidence" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-05", Description = "Review related transactions" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-06", Description = "Check tax treatment" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-07", Description = "Check reconciliation impact" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-08", Description = "Evaluate materiality & quantitative impact" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-09", Description = "Check prior audit run & year recurrence" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-10", Description = "Verify voucher narration & documentation" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-11", Description = "Perform root-cause pattern classification" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-12", Description = "Record investigation remarks" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-13", Description = "Attach supporting evidence" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-14", Description = "Link working papers" },
            new() { Id = Guid.NewGuid().ToString(), InvestigationId = investigationId, Code = "INV-CHK-15", Description = "Record conclusion" }
        };
    }
}
