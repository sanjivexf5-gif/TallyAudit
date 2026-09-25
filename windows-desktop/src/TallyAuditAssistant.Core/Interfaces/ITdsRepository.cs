using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Tds;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ITdsRepository
{
    Task<IReadOnlyList<TdsRuleDefinition>> GetRulesAsync(CancellationToken cancellationToken = default);
    Task<TdsRuleDefinition?> GetRuleByIdAsync(string ruleId, CancellationToken cancellationToken = default);
    Task SaveRuleAsync(TdsRuleDefinition rule, CancellationToken cancellationToken = default);
    Task SetRuleEnabledAsync(string ruleId, bool enabled, CancellationToken cancellationToken = default);
    Task UpdateRuleParametersAsync(string ruleId, string parametersJson, CancellationToken cancellationToken = default);

    Task SaveResultsBatchAsync(IEnumerable<TdsCheckResult> results, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TdsCheckResult>> GetResultsAsync(string companyId, string? ruleId = null, string? section = null, SeverityLevel? severity = null, TdsCheckStatus? status = null, CancellationToken cancellationToken = default);
    Task<TdsAuditSummary> GetSummaryAsync(string companyId, CancellationToken cancellationToken = default);
    Task<TdsVoucherDetail?> GetVoucherDetailAsync(string voucherId, CancellationToken cancellationToken = default);
}

public interface ITdsExceptionService
{
    Task UpdateStatusAsync(string resultId, ReviewStatus status, string reviewer, string? notes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TdsCheckResult>> GetExceptionsByVoucherAsync(string voucherId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TdsCheckResult>> SearchExceptionsAsync(string companyId, string query, CancellationToken cancellationToken = default);
}
