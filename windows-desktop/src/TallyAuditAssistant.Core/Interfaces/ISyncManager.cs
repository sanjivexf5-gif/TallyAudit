using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Ledgers;
using TallyAuditAssistant.Core.Domain.Sync;
using TallyAuditAssistant.Core.Domain.Vouchers;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ISyncManager
{
    SyncStatus CurrentStatus { get; }
    SyncMetrics CurrentMetrics { get; }
    
    event EventHandler<SyncMetrics>? ProgressChanged;
    event EventHandler<string>? SyncLogEmitted;

    Task<SyncResult> StartSyncAsync(string companyName, SyncMode mode, CancellationToken cancellationToken = default);
    Task PauseAsync();
    Task ResumeAsync();
    Task CancelAsync();
    Task<SyncResult> RetryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SyncHistoryRecord>> GetSyncHistoryAsync(string companyId, CancellationToken cancellationToken = default);
}

public interface ISyncRepository
{
    Task UpsertCompanyAsync(Company company, FinancialYear fy, CancellationToken cancellationToken = default);
    Task<int> BatchUpsertGroupsAsync(IReadOnlyList<Group> groups, string companyId, CancellationToken cancellationToken = default);
    Task<(int inserted, int updated)> BatchUpsertLedgersAsync(IReadOnlyList<Ledger> ledgers, string companyId, CancellationToken cancellationToken = default);
    Task<int> BatchUpsertVoucherTypesAsync(IReadOnlyList<VoucherType> types, string companyId, CancellationToken cancellationToken = default);
    Task<(int inserted, int updated)> BatchUpsertVouchersAsync(IReadOnlyList<Voucher> vouchers, string companyId, CancellationToken cancellationToken = default);
    Task RecordSyncHistoryAsync(SyncHistoryRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SyncHistoryRecord>> GetSyncHistoryAsync(string companyId, int limit = 20, CancellationToken cancellationToken = default);
    Task OptimizeIndexesAsync(CancellationToken cancellationToken = default);
}
