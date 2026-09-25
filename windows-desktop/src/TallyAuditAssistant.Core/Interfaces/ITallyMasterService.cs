using TallyAuditAssistant.Core.Domain.Tally;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ITallyMasterService
{
    Task<IReadOnlyList<TallyLedgerDto>> GetLedgersAsync(string companyName, long? fromAlterId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetGroupsAsync(string companyName, CancellationToken cancellationToken = default);
}
