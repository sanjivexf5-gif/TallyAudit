using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Tds;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ITdsRule
{
    string RuleId { get; }
    string Name { get; }
    string Section { get; }
    string Description { get; }
    DateTime EffectiveDate { get; }
    DateTime? ExpiryDate { get; }
    string Jurisdiction { get; }
    string Version { get; }
    string SourceReference { get; }
    SeverityLevel Severity { get; set; }
    bool Enabled { get; set; }
    Dictionary<string, object> Parameters { get; set; }

    Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default);
}
