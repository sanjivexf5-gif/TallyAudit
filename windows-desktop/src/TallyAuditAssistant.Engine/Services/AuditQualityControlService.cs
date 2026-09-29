using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Engine.Services;

public class AuditQualityControlService : IAuditQualityControlService
{
    private readonly IAuditFinalizationRepository _finalizationRepository;
    private readonly IAuditFinalizationService _finalizationService;
    private readonly IAuditRepository _auditRepository;
    private readonly IAuditTrailService _auditTrailService;

    public AuditQualityControlService(
        IAuditFinalizationRepository finalizationRepository,
        IAuditFinalizationService finalizationService,
        IAuditRepository auditRepository,
        IAuditTrailService auditTrailService)
    {
        _finalizationRepository = finalizationRepository;
        _finalizationService = finalizationService;
        _auditRepository = auditRepository;
        _auditTrailService = auditTrailService;
    }

    public async Task<AuditQualityControlSummary> GetQualityControlSummaryAsync(string companyId, string financialPeriodId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(financialPeriodId))
        {
            return new AuditQualityControlSummary
            {
                CompanyId = companyId,
                FinancialPeriodId = financialPeriodId,
                EngagementStatus = "Uninitialized",
                PassedChecksCount = 0,
                WarningsCount = 0,
                AttentionRequiredCount = 0,
                BlockedCount = 0,
                IsReadyForReview = false,
                IsReadyForFinalization = false,
                Checks = new List<QualityControlCheckItem>
                {
                    new()
                    {
                        Name = "Engagement Scope",
                        Category = "Data Completeness",
                        Status = "Blocked",
                        Explanation = "No company or financial period selected.",
                        Severity = "Critical",
                        RecommendedAction = "Please select a company and active financial year."
                    }
                }
            };
        }

        // Get or create state so we have a valid finalization record
        var state = await _finalizationService.GetOrCreateStateAsync(companyId, financialPeriodId, cancellationToken);
        var checklist = await _finalizationRepository.GetChecklistAsync(state.Id, cancellationToken);
        var openItems = await _finalizationRepository.GetOpenItemsAsync(state.Id, cancellationToken);
        var reviewNotes = await _finalizationRepository.GetReviewNotesAsync(state.Id, cancellationToken);
        
        // Fetch up to 1000 exceptions safely
        var exceptions = await _auditRepository.GetExceptionsAsync(companyId, take: 1000, cancellationToken: cancellationToken);
        var vouchersCount = await _auditRepository.GetVoucherCountAsync(companyId, cancellationToken);

        var checks = new List<QualityControlCheckItem>();

        // Check 1: Company and Period Selection
        checks.Add(new QualityControlCheckItem
        {
            Name = "Company and Period Selection",
            Category = "Data Completeness",
            Status = "Pass",
            Explanation = $"Active engagement loaded for company ID {companyId} and financial period {financialPeriodId}.",
            Severity = "Low",
            RecommendedAction = "No action required."
        });

        // Check 2: Data Availability & Synchronization
        bool hasData = vouchersCount > 0;
        checks.Add(new QualityControlCheckItem
        {
            Name = "Data Availability & Synchronization",
            Category = "Data Completeness",
            Status = hasData ? "Pass" : "Warning",
            Explanation = hasData ? $"Synchronized database contains {vouchersCount} transaction vouchers." : "No vouchers synced from TallyPrime.",
            Severity = hasData ? "Low" : "High",
            RecommendedAction = hasData ? "No action required." : "Please trigger a full Tally synchronization."
        });

        // Check 3: Audit Plan (SA 300)
        var planItem = checklist.FirstOrDefault(i => i.Code == "PLAN-01");
        bool hasPlan = planItem?.IsCompleted ?? false;
        checks.Add(new QualityControlCheckItem
        {
            Name = "Engagement Planning (SA 300)",
            Category = "Audit Execution",
            Status = hasPlan ? "Pass" : "Attention Required",
            Explanation = hasPlan ? "Audit plan is documented and signed off." : "Audit planning is incomplete.",
            Severity = hasPlan ? "Low" : "Medium",
            RelatedEntityReference = planItem?.Id ?? string.Empty,
            RecommendedAction = "Mark planning section completed in completion checklist."
        });

        // Check 4: Risk Assessment (SA 315)
        var riskItem = checklist.FirstOrDefault(i => i.Code == "RISK-01");
        bool hasRisk = riskItem?.IsCompleted ?? false;
        checks.Add(new QualityControlCheckItem
        {
            Name = "Risk Assessment (SA 315)",
            Category = "Audit Execution",
            Status = hasRisk ? "Pass" : "Attention Required",
            Explanation = hasRisk ? "Scope risks and material areas analyzed." : "Risk assessment documentation outstanding.",
            Severity = hasRisk ? "Low" : "Medium",
            RelatedEntityReference = riskItem?.Id ?? string.Empty,
            RecommendedAction = "Document key risks in working papers."
        });

        // Check 5: Materiality (SA 320)
        var matItem = checklist.FirstOrDefault(i => i.Code == "MAT-01");
        bool hasMat = matItem?.IsCompleted ?? false;
        checks.Add(new QualityControlCheckItem
        {
            Name = "Materiality Assessment (SA 320)",
            Category = "Audit Execution",
            Status = hasMat ? "Pass" : "Attention Required",
            Explanation = hasMat ? "Materiality thresholds recorded." : "Professional materiality amounts are not documented.",
            Severity = hasMat ? "Low" : "Medium",
            RelatedEntityReference = matItem?.Id ?? string.Empty,
            RecommendedAction = "Record bench mark turnover and calculate overall materiality."
        });

        // Check 6: Unreviewed Findings (SA 250)
        int unreviewedCount = exceptions.Count(e => e.Status == ReviewStatus.Pending || e.Status == ReviewStatus.RequiresReview);
        checks.Add(new QualityControlCheckItem
        {
            Name = "Findings and Exceptions Review",
            Category = "Findings",
            Status = unreviewedCount == 0 ? "Pass" : "Attention Required",
            Explanation = unreviewedCount == 0 ? "All identified exceptions have been reviewed." : $"{unreviewedCount} exceptions remain unreviewed.",
            Severity = unreviewedCount == 0 ? "Low" : "High",
            RecommendedAction = unreviewedCount == 0 ? "No action required." : "Go to Findings section to Accept/Resolve all open exceptions."
        });

        // Check 7: Missing Evidence
        var evidenceItem = checklist.FirstOrDefault(i => i.Code == "EVI-01");
        bool hasEvidence = evidenceItem?.IsCompleted ?? false;
        checks.Add(new QualityControlCheckItem
        {
            Name = "Supporting Evidence Registry",
            Category = "Evidence",
            Status = hasEvidence ? "Pass" : "Warning",
            Explanation = hasEvidence ? "Substantive audit evidence attached." : "Voucher supporting files or physical receipts missing.",
            Severity = hasEvidence ? "Low" : "Medium",
            RelatedEntityReference = evidenceItem?.Id ?? string.Empty,
            RecommendedAction = "Upload sample vouchers to the evidence workspace."
        });

        // Check 8: Open Items
        int openItemsCount = openItems.Count(i => i.Status == "Open");
        bool hasHighPriorityOpenItems = openItems.Any(i => i.Status == "Open" && i.Priority == "High");
        string openItemStatus = "Pass";
        if (hasHighPriorityOpenItems) openItemStatus = "Blocked";
        else if (openItemsCount > 0) openItemStatus = "Attention Required";

        checks.Add(new QualityControlCheckItem
        {
            Name = "Outstanding Engagement Tasks",
            Category = "Review",
            Status = openItemStatus,
            Explanation = openItemsCount == 0 ? "No outstanding engagement actions." : $"{openItemsCount} active open items remain.",
            Severity = hasHighPriorityOpenItems ? "Critical" : (openItemsCount > 0 ? "Medium" : "Low"),
            RecommendedAction = openItemsCount == 0 ? "No action required." : "Resolve open notes and pending corrections."
        });

        // Check 9: Reviewer Notes
        int openNotesCount = reviewNotes.Count(n => n.Status == "Open" || n.Status == "Responded");
        checks.Add(new QualityControlCheckItem
        {
            Name = "Review Notes",
            Category = "Review",
            Status = openNotesCount == 0 ? "Pass" : "Attention Required",
            Explanation = openNotesCount == 0 ? "All reviewer notes are resolved." : $"{openNotesCount} unresolved reviewer comments found.",
            Severity = openNotesCount == 0 ? "Low" : "High",
            RecommendedAction = openNotesCount == 0 ? "No action required." : "Address and close out reviewer notes."
        });

        // Calculate readiness
        bool isReadyForReview = hasPlan && hasRisk && hasMat && hasData;
        bool isReadyForFinalization = isReadyForReview && 
                                      checklist.All(i => i.IsCompleted) && 
                                      openItemsCount == 0 && 
                                      openNotesCount == 0 && 
                                      unreviewedCount == 0;

        // Check 10: Finalization Status
        bool isFinalized = state.Status == AuditLifecycleStatus.Finalized;
        checks.Add(new QualityControlCheckItem
        {
            Name = "Finalization Status",
            Category = "Finalization",
            Status = isFinalized ? "Pass" : (isReadyForFinalization ? "Warning" : "Blocked"),
            Explanation = isFinalized ? "Audit permanent file finalized." : (isReadyForFinalization ? "Audit ready for finalization." : "Blockers prevent permanent finalization."),
            Severity = isFinalized ? "Low" : "High",
            RecommendedAction = isFinalized ? "No action required." : (isReadyForFinalization ? "Run the permanent file locking procedure." : "Satisfy all completion checks first.")
        });

        var summary = new AuditQualityControlSummary
        {
            CompanyId = companyId,
            FinancialPeriodId = financialPeriodId,
            EngagementStatus = state.Status.ToString(),
            PassedChecksCount = checks.Count(c => c.Status == "Pass"),
            WarningsCount = checks.Count(c => c.Status == "Warning"),
            AttentionRequiredCount = checks.Count(c => c.Status == "Attention Required"),
            BlockedCount = checks.Count(c => c.Status == "Blocked"),
            IsReadyForReview = isReadyForReview,
            IsReadyForFinalization = isReadyForFinalization,
            Checks = checks
        };

        await _auditTrailService.RecordActivityAsync(
            "QCCheckGenerated",
            "QC_DASHBOARD",
            $"Quality control checks generated for company {companyId}, period {financialPeriodId}.",
            companyId,
            financialPeriodId,
            ct: cancellationToken
        );

        return summary;
    }
}
