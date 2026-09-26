using System.Text.Json;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Gst;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Gst;

public abstract class BaseGstRule : IGstRule
{
    protected readonly SqliteConnectionFactory ConnectionFactory;

    public abstract string RuleId { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public virtual DateTime EffectiveDate { get; set; } = new DateTime(2017, 7, 1);
    public virtual DateTime? ExpiryDate { get; set; }
    public virtual string Jurisdiction { get; set; } = "IN-ALL";
    public virtual string Version { get; set; } = "1.0.0";
    public virtual string SourceReference { get; set; } = "CGST Act 2017";
    public SeverityLevel Severity { get; set; }
    public bool Enabled { get; set; } = true;
    public Dictionary<string, object> Parameters { get; set; } = new();

    protected BaseGstRule(SqliteConnectionFactory connectionFactory, SeverityLevel defaultSeverity = SeverityLevel.Medium)
    {
        ConnectionFactory = connectionFactory;
        Severity = defaultSeverity;
    }

    public abstract Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default);

    protected T GetParam<T>(string key, T defaultValue)
    {
        if (!Parameters.TryGetValue(key, out var val)) return defaultValue;
        if (val is JsonElement json)
        {
            try
            {
                if (typeof(T) == typeof(double) || typeof(T) == typeof(decimal))
                    return (T)(object)Convert.ChangeType(json.GetDouble(), typeof(T));
                if (typeof(T) == typeof(int))
                    return (T)(object)Convert.ChangeType(json.GetInt32(), typeof(T));
                if (typeof(T) == typeof(string))
                    return (T)(object)json.GetString()!;
                if (typeof(T) == typeof(bool))
                    return (T)(object)json.GetBoolean();
            }
            catch { return defaultValue; }
        }
        try { return (T)Convert.ChangeType(val, typeof(T)); }
        catch { return defaultValue; }
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
        var val = GetCol(row, colName);
        if (val == null) return defaultVal;
        if (val is decimal d) return d;
        try { return Convert.ToDecimal(val); } catch { return defaultVal; }
    }

    protected static decimal? GetNullableDecimal(object? row, string colName)
    {
        var val = GetCol(row, colName);
        if (val == null) return null;
        if (val is decimal d) return d;
        try { return Convert.ToDecimal(val); } catch { return null; }
    }

    protected static DateTime? GetDateTime(object? row, string colName)
    {
        var val = GetCol(row, colName);
        if (val == null) return null;
        if (val is DateTime dt) return dt;
        if (DateTime.TryParse(val.ToString(), out var parsed)) return parsed;
        return null;
    }

    protected static long GetLong(object? row, string colName, long defaultVal = 0L)
    {
        var val = GetCol(row, colName);
        if (val == null) return defaultVal;
        try { return Convert.ToInt64(val); } catch { return defaultVal; }
    }

    protected GstCheckResult CreateException(
        string companyId,
        string explanation,
        SeverityLevel severity,
        string? voucherId = null,
        string? voucherNumber = null,
        DateTime? voucherDate = null,
        string? voucherTypeName = null,
        string? partyName = null,
        string? partyGstin = null,
        decimal? taxableAmount = null,
        decimal? taxAmount = null,
        decimal? cgst = null,
        decimal? sgst = null,
        decimal? igst = null,
        string? placeOfSupply = null,
        object? evidence = null)
    {
        return new GstCheckResult
        {
            RuleId = RuleId,
            RuleName = Name,
            CompanyId = companyId,
            VoucherId = voucherId,
            VoucherNumber = voucherNumber,
            VoucherDate = voucherDate,
            VoucherTypeName = voucherTypeName,
            PartyLedgerName = partyName,
            PartyGstin = partyGstin,
            TaxableAmount = taxableAmount,
            TaxAmount = taxAmount,
            CgstAmount = cgst,
            SgstAmount = sgst,
            IgstAmount = igst,
            PlaceOfSupply = placeOfSupply,
            Severity = severity,
            Status = GstCheckStatus.Exception,
            Explanation = explanation,
            EvidenceJson = evidence != null ? JsonSerializer.Serialize(evidence) : "{}",
            Jurisdiction = Jurisdiction,
            SourceReference = SourceReference,
            DetectedAt = DateTime.UtcNow
        };
    }

    protected GstCheckResult CreateUnableToDetermine(
        string companyId,
        string reason,
        string? voucherId = null,
        string? voucherNumber = null,
        DateTime? voucherDate = null,
        string? voucherTypeName = null,
        string? partyName = null,
        object? evidence = null)
    {
        return new GstCheckResult
        {
            RuleId = RuleId,
            RuleName = Name,
            CompanyId = companyId,
            VoucherId = voucherId,
            VoucherNumber = voucherNumber,
            VoucherDate = voucherDate,
            VoucherTypeName = voucherTypeName,
            PartyLedgerName = partyName,
            Severity = SeverityLevel.Informational,
            Status = GstCheckStatus.UnableToDetermine,
            Explanation = $"Unable to determine: {reason}",
            EvidenceJson = evidence != null ? JsonSerializer.Serialize(evidence) : "{}",
            Jurisdiction = Jurisdiction,
            SourceReference = SourceReference,
            DetectedAt = DateTime.UtcNow
        };
    }

    protected GstCheckResult CreatePassed(
        string companyId,
        string? voucherId = null,
        string? voucherNumber = null,
        DateTime? voucherDate = null,
        string? voucherTypeName = null,
        string? partyName = null)
    {
        return new GstCheckResult
        {
            RuleId = RuleId,
            RuleName = Name,
            CompanyId = companyId,
            VoucherId = voucherId,
            VoucherNumber = voucherNumber,
            VoucherDate = voucherDate,
            VoucherTypeName = voucherTypeName,
            PartyLedgerName = partyName,
            Severity = SeverityLevel.Informational,
            Status = GstCheckStatus.Passed,
            Explanation = "Check passed successfully against statutory rule specifications.",
            Jurisdiction = Jurisdiction,
            SourceReference = SourceReference,
            DetectedAt = DateTime.UtcNow
        };
    }
}
