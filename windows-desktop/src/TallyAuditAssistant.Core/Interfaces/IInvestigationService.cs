using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IInvestigationService
{
    Task<ExceptionInvestigation> GetOrCreateInvestigationAsync(
        string exceptionId, 
        string companyId, 
        string username, 
        string? financialPeriodId = null, 
        string? auditRunId = null, 
        CancellationToken ct = default);

    Task<ExceptionInvestigation?> GetInvestigationByIdAsync(string investigationId, CancellationToken ct = default);
    Task<ExceptionInvestigation?> GetInvestigationByExceptionIdAsync(string exceptionId, CancellationToken ct = default);
    Task<IReadOnlyList<ExceptionInvestigation>> GetInvestigationsForCompanyAsync(string companyId, InvestigationStatus? status = null, CancellationToken ct = default);

    bool CanTransition(InvestigationStatus currentStatus, InvestigationStatus targetStatus);
    IReadOnlyList<InvestigationStatus> GetAllowedTransitions(InvestigationStatus currentStatus);

    Task<bool> TransitionStatusAsync(
        string investigationId, 
        InvestigationStatus newStatus, 
        string username, 
        string? reason = null, 
        CancellationToken ct = default);

    Task UpdateInvestigationAsync(ExceptionInvestigation investigation, string username, CancellationToken ct = default);

    Task SaveConclusionAsync(
        string investigationId, 
        InvestigationConclusion conclusion, 
        string? notes, 
        string username, 
        CancellationToken ct = default);

    Task LinkEvidenceAsync(
        string investigationId, 
        string evidenceId, 
        string username, 
        CancellationToken ct = default);

    Task LinkWorkingPaperAsync(
        string investigationId, 
        string workingPaperId, 
        string username, 
        CancellationToken ct = default);

    Task ToggleChecklistItemAsync(
        string itemId, 
        bool isCompleted, 
        string username, 
        string? notes = null, 
        CancellationToken ct = default);

    Task<InvestigationRelatedData> GetRelatedDataAsync(string exceptionId, string companyId, CancellationToken ct = default);
}
