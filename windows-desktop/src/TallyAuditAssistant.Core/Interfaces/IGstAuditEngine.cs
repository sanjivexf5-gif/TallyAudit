using TallyAuditAssistant.Core.Domain.Gst;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IGstAuditEngine
{
    IReadOnlyList<IGstRule> RegisteredRules { get; }
    void RegisterRule(IGstRule rule);
    Task<GstAuditSummary> ExecuteAuditAsync(GstAuditContext context, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GstCheckResult>> EvaluateRuleAsync(string ruleId, GstAuditContext context, CancellationToken cancellationToken = default);
    event EventHandler<GstAuditProgress>? ProgressChanged;
}
