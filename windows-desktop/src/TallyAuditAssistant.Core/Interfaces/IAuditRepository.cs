using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Ledgers;
using TallyAuditAssistant.Core.Domain.Vouchers;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ApplyMigrationsAsync(CancellationToken cancellationToken = default);
    string DatabasePath { get; }
}

public interface IAuditRepository
{
    Task<IReadOnlyList<Company>> GetAllCompaniesAsync(CancellationToken cancellationToken = default);
    Task<Company?> GetCompanyByIdAsync(string companyId, CancellationToken cancellationToken = default);
    Task SaveCompanyAsync(Company company, CancellationToken cancellationToken = default);

    Task<int> GetVoucherCountAsync(string companyId, CancellationToken cancellationToken = default);
    Task<int> GetLedgerCountAsync(string companyId, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<AuditException>> GetExceptionsAsync(
        string companyId, 
        RuleCategory? category = null, 
        SeverityLevel? minSeverity = null, 
        ReviewStatus? status = null, 
        int skip = 0, 
        int take = 50, 
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditException>> GetExceptionsFilteredAsync(
        string companyId,
        string? category = null,
        string? severity = null,
        string? status = null,
        string? searchQuery = null,
        string? sortBy = null,
        bool isDescending = true,
        CancellationToken cancellationToken = default);

    Task<int> GetExceptionCountAsync(string companyId, RuleCategory? category = null, SeverityLevel? minSeverity = null, ReviewStatus? status = null, CancellationToken cancellationToken = default);
    Task UpdateExceptionStatusAsync(string exceptionId, ReviewStatus newStatus, string? auditorNote, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<AuditRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default);

    Task SaveAuditRunAsync(AuditRun run, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditRun>> GetAuditRunsAsync(string companyId, CancellationToken cancellationToken = default);
}

public interface ISettingsService
{
    Task<string> GetSettingAsync(string key, string defaultValue = "", CancellationToken cancellationToken = default);
    Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<int> GetTallyPortAsync();
    Task SetTallyPortAsync(int port);
    Task<string> GetTallyHostAsync();
    Task SetTallyHostAsync(string host);
    Task<bool> IsMockModeEnabledAsync();
    Task SetMockModeEnabledAsync(bool enabled);
}
