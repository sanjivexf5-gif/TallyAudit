using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Gst;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IGstRule
{
    string RuleId { get; }
    string Name { get; }
    string Description { get; }
    DateTime EffectiveDate { get; }
    DateTime? ExpiryDate { get; }
    string Jurisdiction { get; }
    string Version { get; }
    string SourceReference { get; }
    SeverityLevel Severity { get; set; }
    bool Enabled { get; set; }
    Dictionary<string, object> Parameters { get; set; }

    Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default);
}
