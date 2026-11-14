using TallyAuditAssistant.Core.Domain.Practice;

namespace TallyAuditAssistant.Core.Interfaces;

/// <summary>Persistence boundary for CA-firm clients, legal entities and service engagements.</summary>
public interface IPracticeRepository
{
    Task<IReadOnlyList<PracticeClient>> GetClientsAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<PracticeClient?> GetClientByIdAsync(string id, CancellationToken ct = default);
    Task<bool> CreateClientAsync(PracticeClient client, CancellationToken ct = default);
    Task<bool> UpdateClientAsync(PracticeClient client, CancellationToken ct = default);

    Task<IReadOnlyList<PracticeClientEntity>> GetClientEntitiesAsync(string clientId, CancellationToken ct = default);
    Task<bool> CreateClientEntityAsync(PracticeClientEntity entity, CancellationToken ct = default);
    Task<bool> UpdateClientEntityAsync(PracticeClientEntity entity, CancellationToken ct = default);

    Task<IReadOnlyList<ClientEngagement>> GetEngagementsAsync(string? clientId = null, string? status = null, CancellationToken ct = default);
    Task<ClientEngagement?> GetEngagementByIdAsync(string id, CancellationToken ct = default);
    Task<bool> CreateEngagementAsync(ClientEngagement engagement, CancellationToken ct = default);
    Task<bool> UpdateEngagementAsync(ClientEngagement engagement, CancellationToken ct = default);\n    Task<IReadOnlyList<RecurringComplianceTaskTemplate>> GetRecurringTaskTemplatesAsync(string? clientId = null, bool includeInactive = false, CancellationToken ct = default);\n    Task<bool> CreateRecurringTaskTemplateAsync(RecurringComplianceTaskTemplate template, CancellationToken ct = default);\n    Task<bool> UpdateRecurringTaskTemplateAsync(RecurringComplianceTaskTemplate template, CancellationToken ct = default);
}
