using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Gst;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IGstRepository
{
    Task<IReadOnlyList<GstRuleDefinition>> GetRulesAsync(CancellationToken cancellationToken = default);
    Task<GstRuleDefinition?> GetRuleByIdAsync(string ruleId, CancellationToken cancellationToken = default);
    Task SaveRuleAsync(GstRuleDefinition rule, CancellationToken cancellationToken = default);
    Task SetRuleEnabledAsync(string ruleId, bool enabled, CancellationToken cancellationToken = default);
    Task UpdateRuleParametersAsync(string ruleId, string parametersJson, CancellationToken cancellationToken = default);

    Task SaveResultsBatchAsync(IEnumerable<GstCheckResult> results, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GstCheckResult>> GetResultsAsync(string companyId, string? ruleId = null, SeverityLevel? severity = null, GstCheckStatus? status = null, CancellationToken cancellationToken = default);
    Task<GstAuditSummary> GetSummaryAsync(string companyId, CancellationToken cancellationToken = default);
    Task<GstVoucherDetail?> GetVoucherDetailAsync(string voucherId, CancellationToken cancellationToken = default);
}

public interface IGstExceptionService
{
    Task UpdateStatusAsync(string resultId, ReviewStatus status, string reviewer, string? notes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GstCheckResult>> GetExceptionsByVoucherAsync(string voucherId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GstCheckResult>> SearchExceptionsAsync(string companyId, string query, CancellationToken cancellationToken = default);
}
