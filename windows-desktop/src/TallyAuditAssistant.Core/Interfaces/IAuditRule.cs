using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

public record AuditExecutionContext(
    string CompanyId,
    DateTime FromDate,
    DateTime ToDate,
    IReadOnlyDictionary<string, object>? Options = null);

public interface IAuditRule
{
    string RuleId { get; }
    string Name { get; }
    RuleCategory Category { get; }
    string Description { get; }
    SeverityLevel Severity { get; set; }
    string Version { get; }
    DateTime? EffectiveFrom { get; }
    DateTime? EffectiveTo { get; }
    bool Enabled { get; set; }
    Dictionary<string, object> Parameters { get; set; }

    Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default);
}
