using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Corrections;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ITallyCorrectionRepository
{
    Task SaveCorrectionAsync(TallyCorrection correction, CancellationToken cancellationToken = default);
    Task<TallyCorrection?> GetCorrectionByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TallyCorrection>> GetCorrectionsAsync(string companyId, CorrectionStatus? statusFilter = null, CancellationToken cancellationToken = default);
    Task UpdateCorrectionStatusAsync(string id, CorrectionStatus status, string? updatedBy, string? note, CancellationToken cancellationToken = default);
    Task RecordCorrectionAuditTrailAsync(string correctionId, string companyId, string action, string actor, string details, string correlationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TallyCorrectionAuditEntry>> GetCorrectionAuditTrailAsync(string correctionId, CancellationToken cancellationToken = default);
}
