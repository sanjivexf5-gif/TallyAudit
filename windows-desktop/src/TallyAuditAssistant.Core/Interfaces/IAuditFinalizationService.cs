using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IAuditFinalizationService
{
    Task<AuditFinalizationState> GetOrCreateStateAsync(string companyId, string financialPeriodId, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(string id, AuditLifecycleStatus status, string user, string? comments = null, CancellationToken cancellationToken = default);
    Task SubmitForReviewAsync(string id, string auditor, string reviewer, CancellationToken cancellationToken = default);
    Task ReturnWithNotesAsync(string id, string reviewer, string comment, CancellationToken cancellationToken = default);
    Task ApproveForFinalizationAsync(string id, string reviewer, string comment, CancellationToken cancellationToken = default);
    Task FinalizeAuditAsync(string id, string user, CancellationToken cancellationToken = default);
    Task ReopenAuditAsync(string id, string user, string reason, CancellationToken cancellationToken = default);
    
    // Checklist/Open items helpers
    Task InitializeDefaultChecklistAsync(string auditId, CancellationToken cancellationToken = default);
    Task SetChecklistItemCompletedAsync(string itemId, bool isCompleted, string user, string notes, CancellationToken cancellationToken = default);
    Task AddOpenItemAsync(OpenItem item, CancellationToken cancellationToken = default);
    Task AddReviewNoteAsync(ReviewNote note, CancellationToken cancellationToken = default);
}
