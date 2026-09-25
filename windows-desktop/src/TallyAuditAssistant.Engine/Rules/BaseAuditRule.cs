using System.Text.Json;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Rules;

public abstract class BaseAuditRule : IAuditRule
{
    protected readonly SqliteConnectionFactory ConnectionFactory;

    public abstract string RuleId { get; }
    public abstract string Name { get; }
    public abstract RuleCategory Category { get; }
    public abstract string Description { get; }
    public SeverityLevel Severity { get; set; }
    public virtual string Version => "1.0.0";
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool Enabled { get; set; } = true;
    public Dictionary<string, object> Parameters { get; set; } = new();

    protected BaseAuditRule(SqliteConnectionFactory connectionFactory, SeverityLevel defaultSeverity)
    {
        ConnectionFactory = connectionFactory;
        Severity = defaultSeverity;
    }

    public abstract Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default);

    protected T GetParam<T>(string key, T defaultValue)
    {
        if (Parameters.TryGetValue(key, out var val))
        {
            if (val is T typedVal) return typedVal;
            if (val is JsonElement jsonEl)
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(jsonEl.GetRawText()) ?? defaultValue;
                }
                catch { return defaultValue; }
            }
            try
            {
                return (T)Convert.ChangeType(val, typeof(T));
            }
            catch { return defaultValue; }
        }
        return defaultValue;
    }

    protected AuditResult CreateResult(
        string companyId,
        string explanation,
        SeverityLevel severity,
        string? voucherId = null,
        string? ledgerId = null,
        string? voucherNumber = null,
        DateTime? voucherDate = null,
        decimal? flaggedAmount = null,
        object? evidenceObj = null)
    {
        return new AuditResult
        {
            RuleId = RuleId,
            RuleName = Name,
            CompanyId = companyId,
            VoucherId = voucherId,
            LedgerId = ledgerId,
            VoucherNumber = voucherNumber,
            VoucherDate = voucherDate,
            FlaggedAmount = flaggedAmount,
            Severity = severity,
            Explanation = explanation,
            Evidence = evidenceObj != null ? JsonSerializer.Serialize(evidenceObj) : "{}",
            Status = ReviewStatus.Pending,
            DetectedAt = DateTime.UtcNow
        };
    }
}
