using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IAuditTrailService
{
    Task RecordActivityAsync(
        string action,
        string category,
        string description,
        string? companyId = null,
        string? financialPeriodId = null,
        string? entityType = null,
        string? entityId = null,
        string? metadataJson = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<AuditTrailEntry>> GetAuditTrailAsync(
        string? companyId = null,
        string? financialPeriodId = null,
        int limit = 100,
        CancellationToken ct = default);
}
