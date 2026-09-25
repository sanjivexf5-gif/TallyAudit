using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

public record AuditEngineProgress(
    string CurrentRuleId,
    string CurrentRuleName,
    int RulesExecuted,
    int TotalRules,
    int ExceptionsFound,
    double Percentage);

public interface IAuditEngine
{
    IReadOnlyList<IAuditRule> RegisteredRules { get; }
    void RegisterRule(IAuditRule rule);
    
    event EventHandler<AuditEngineProgress>? ProgressChanged;

    Task<IReadOnlyList<AuditResult>> ExecuteAuditAsync(AuditExecutionContext context, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditResult>> ExecuteCategoryAsync(RuleCategory category, AuditExecutionContext context, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditResult>> ExecuteRuleAsync(string ruleId, AuditExecutionContext context, CancellationToken cancellationToken = default);
}
