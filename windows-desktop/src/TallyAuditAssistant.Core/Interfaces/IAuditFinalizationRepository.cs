using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IAuditFinalizationRepository
{
    Task<AuditFinalizationState?> GetStateAsync(string companyId, string financialPeriodId, CancellationToken cancellationToken = default);
    Task<AuditFinalizationState?> GetStateByIdAsync(string id, CancellationToken cancellationToken = default);
    Task SaveStateAsync(AuditFinalizationState state, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<ChecklistItem>> GetChecklistAsync(string auditId, CancellationToken cancellationToken = default);
    Task<ChecklistItem?> GetChecklistItemByIdAsync(string itemId, CancellationToken cancellationToken = default);
    Task SaveChecklistItemAsync(ChecklistItem item, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<OpenItem>> GetOpenItemsAsync(string auditId, CancellationToken cancellationToken = default);
    Task SaveOpenItemAsync(OpenItem item, CancellationToken cancellationToken = default);
    Task DeleteOpenItemAsync(string id, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<ReviewNote>> GetReviewNotesAsync(string auditId, CancellationToken cancellationToken = default);
    Task SaveReviewNoteAsync(ReviewNote note, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<AuditAmendment>> GetAmendmentsAsync(string auditId, CancellationToken cancellationToken = default);
    Task<string> GetOrCreateWorkingPaperPlanIdAsync(string companyId, string companyName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkingPaper>> GetWorkingPapersAsync(string planId, CancellationToken cancellationToken = default);
    Task SaveWorkingPaperAsync(WorkingPaper paper, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkingPaperAttachment>> GetWorkingPaperAttachmentsAsync(string workingPaperId, CancellationToken cancellationToken = default);
    Task SaveWorkingPaperAttachmentAsync(WorkingPaperAttachment attachment, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEvidence>> GetAuditEvidenceAsync(string planId, CancellationToken cancellationToken = default);
    Task SaveAuditEvidenceAsync(AuditEvidence evidence, CancellationToken cancellationToken = default);
    Task SaveAmendmentAsync(AuditAmendment amendment, CancellationToken cancellationToken = default);
}
