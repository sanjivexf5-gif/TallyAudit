using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IInvestigationRepository
{
    Task<ExceptionInvestigation?> GetByIdAsync(string investigationId, CancellationToken ct = default);
    Task<ExceptionInvestigation?> GetByExceptionIdAsync(string exceptionId, CancellationToken ct = default);
    Task<IReadOnlyList<ExceptionInvestigation>> GetByCompanyIdAsync(string companyId, InvestigationStatus? status = null, CancellationToken ct = default);
    Task SaveInvestigationAsync(ExceptionInvestigation investigation, CancellationToken ct = default);
    Task UpdateInvestigationAsync(ExceptionInvestigation investigation, CancellationToken ct = default);
    Task UpdateStatusAsync(string investigationId, InvestigationStatus newStatus, string updatedBy, DateTime? closedAt, CancellationToken ct = default);
    Task<IReadOnlyList<InvestigationChecklistItem>> GetChecklistItemsAsync(string investigationId, CancellationToken ct = default);
    Task SaveChecklistItemsAsync(IEnumerable<InvestigationChecklistItem> items, CancellationToken ct = default);
    Task UpdateChecklistItemAsync(InvestigationChecklistItem item, CancellationToken ct = default);
}
