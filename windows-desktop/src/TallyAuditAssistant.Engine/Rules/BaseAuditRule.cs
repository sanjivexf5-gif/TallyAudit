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
                    if (typeof(T) == typeof(decimal))
                        return (T)(object)Convert.ToDecimal(jsonEl.GetDouble());
                    if (typeof(T) == typeof(double))
                        return (T)(object)jsonEl.GetDouble();
                    if (typeof(T) == typeof(int))
                        return (T)(object)jsonEl.GetInt32();
                    if (typeof(T) == typeof(long))
                        return (T)(object)jsonEl.GetInt64();
                    if (typeof(T) == typeof(string))
                        return (T)(object)jsonEl.GetString()!;
                    if (typeof(T) == typeof(bool))
                        return (T)(object)jsonEl.GetBoolean();
                    return JsonSerializer.Deserialize<T>(jsonEl.GetRawText()) ?? defaultValue;
                }
                catch { return defaultValue; }
            }
            try
            {
                if (typeof(T) == typeof(decimal))
                    return (T)(object)Convert.ToDecimal(val);
                return (T)Convert.ChangeType(val, typeof(T));
            }
            catch { return defaultValue; }
        }
        return defaultValue;
    }

    protected static object? GetCol(object? row, string colName)
    {
        if (row is IDictionary<string, object> dict && dict.TryGetValue(colName, out var val))
            return val;
        return null;
    }

    protected static string? GetString(object? row, string colName)
    {
        var val = GetCol(row, colName);
        return val?.ToString();
    }

    protected static decimal GetDecimal(object? row, string colName, decimal defaultVal = 0m)
    {
        return ToDecimal(GetCol(row, colName), defaultVal);
    }

    protected static decimal? GetNullableDecimal(object? row, string colName)
    {
        return ToNullableDecimal(GetCol(row, colName));
    }

    protected static DateTime? GetDateTime(object? row, string colName)
    {
        return ToDateTime(GetCol(row, colName));
    }

    protected static long GetLong(object? row, string colName, long defaultVal = 0L)
    {
        var val = GetCol(row, colName);
        if (val == null) return defaultVal;
        try { return Convert.ToInt64(val); } catch { return defaultVal; }
    }

    protected static decimal ToDecimal(object? val, decimal defaultVal = 0m)
    {
        if (val == null) return defaultVal;
        if (val is decimal d) return d;
        try { return Convert.ToDecimal(val); } catch { return defaultVal; }
    }

    protected static decimal? ToNullableDecimal(object? val)
    {
        if (val == null) return null;
        if (val is decimal d) return d;
        try { return Convert.ToDecimal(val); } catch { return null; }
    }

    protected static DateTime? ToDateTime(object? val)
    {
        if (val == null) return null;
        if (val is DateTime dt) return dt;
        if (DateTime.TryParse(val.ToString(), out var parsed)) return parsed;
        return null;
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
            Category = Category,
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
