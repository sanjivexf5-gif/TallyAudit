using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IAuditRuleRepository
{
    Task<IReadOnlyList<AuditRule>> GetAllRulesAsync(CancellationToken cancellationToken = default);
    Task<AuditRule?> GetRuleByIdAsync(string ruleId, CancellationToken cancellationToken = default);
    Task SaveRuleAsync(AuditRule rule, CancellationToken cancellationToken = default);
    Task SetRuleEnabledAsync(string ruleId, bool isEnabled, CancellationToken cancellationToken = default);
    Task UpdateRuleParametersAsync(string ruleId, string parametersJson, CancellationToken cancellationToken = default);
}

public interface IAuditResultRepository
{
    Task SaveResultsBatchAsync(IReadOnlyList<AuditResult> results, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditResult>> GetResultsAsync(string companyId, string? ruleId = null, SeverityLevel? minSeverity = null, CancellationToken cancellationToken = default);
    Task ClearResultsForCompanyAsync(string companyId, CancellationToken cancellationToken = default);
}

public interface IExceptionRepository
{
    Task<IReadOnlyList<AuditResult>> GetExceptionsAsync(
        string companyId, 
        RuleCategory? category = null, 
        SeverityLevel? minSeverity = null, 
        ReviewStatus? status = null, 
        int skip = 0, 
        int take = 50, 
        CancellationToken cancellationToken = default);

    Task<int> GetExceptionCountAsync(
        string companyId, 
        RuleCategory? category = null, 
        SeverityLevel? minSeverity = null, 
        ReviewStatus? status = null, 
        CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(string resultId, ReviewStatus status, string? reviewer, string? reviewerNote, CancellationToken cancellationToken = default);
}
