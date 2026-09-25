using TallyAuditAssistant.Core.Domain.Tds;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ITdsAuditEngine
{
    IReadOnlyList<ITdsRule> RegisteredRules { get; }
    void RegisterRule(ITdsRule rule);
    Task<TdsAuditSummary> ExecuteAuditAsync(TdsAuditContext context, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TdsCheckResult>> EvaluateRuleAsync(string ruleId, TdsAuditContext context, CancellationToken cancellationToken = default);
    event EventHandler<TdsAuditProgress>? ProgressChanged;
}
