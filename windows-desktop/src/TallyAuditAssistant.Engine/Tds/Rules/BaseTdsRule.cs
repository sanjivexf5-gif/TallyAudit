using System.Text.Json;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Tds;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Tds.Rules;

public abstract class BaseTdsRule : ITdsRule
{
    protected readonly SqliteConnectionFactory ConnectionFactory;

    public abstract string RuleId { get; }
    public abstract string Name { get; }
    public abstract string Section { get; }
    public abstract string Description { get; }
    public virtual DateTime EffectiveDate { get; set; } = new DateTime(2020, 4, 1);
    public virtual DateTime? ExpiryDate { get; set; }
    public virtual string Jurisdiction { get; set; } = "IN-IT-ACT";
    public virtual string Version { get; set; } = "1.0.0";
    public abstract string SourceReference { get; }
    public virtual SeverityLevel Severity { get; set; } = SeverityLevel.Medium;
    public virtual bool Enabled { get; set; } = true;
    public virtual Dictionary<string, object> Parameters { get; set; } = new();

    protected BaseTdsRule(SqliteConnectionFactory connectionFactory)
    {
        ConnectionFactory = connectionFactory;
    }

    public abstract Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default);

    protected T GetParam<T>(string key, T defaultValue)
    {
        if (Parameters.TryGetValue(key, out var val))
        {
            try
            {
                if (val is JsonElement elem)
                {
                    if (typeof(T) == typeof(double) || typeof(T) == typeof(decimal))
                        return (T)(object)(decimal)elem.GetDouble();
                    if (typeof(T) == typeof(int))
                        return (T)(object)elem.GetInt32();
                    if (typeof(T) == typeof(bool))
                        return (T)(object)elem.GetBoolean();
                    if (typeof(T) == typeof(string))
                        return (T)(object)elem.GetString()!;
                }
                return (T)Convert.ChangeType(val, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }

    protected TdsCheckResult CreateResult(
        string companyId,
        string explanation,
        SeverityLevel severity,
        TdsCheckStatus status = TdsCheckStatus.Exception,
        string? voucherId = null,
        string? voucherNumber = null,
        DateTime? voucherDate = null,
        string? voucherTypeName = null,
        string? partyLedgerId = null,
        string? partyLedgerName = null,
        string? partyPan = null,
        string? expenseLedgerName = null,
        decimal? transactionAmount = null,
        decimal? cumulativeVendorAmount = null,
        decimal? sectionThreshold = null,
        decimal? deductedTdsAmount = null,
        decimal? expectedTdsAmount = null,
        decimal? appliedRate = null,
        decimal? expectedRate = null,
        object? evidence = null)
    {
        return new TdsCheckResult
        {
            RuleId = RuleId,
            RuleName = Name,
            Section = Section,
            CompanyId = companyId,
            VoucherId = voucherId,
            VoucherNumber = voucherNumber,
            VoucherDate = voucherDate,
            VoucherTypeName = voucherTypeName,
            PartyLedgerId = partyLedgerId,
            PartyLedgerName = partyLedgerName,
            PartyPan = partyPan,
            ExpenseLedgerName = expenseLedgerName,
            TransactionAmount = transactionAmount,
            CumulativeVendorAmount = cumulativeVendorAmount,
            SectionThreshold = sectionThreshold,
            DeductedTdsAmount = deductedTdsAmount,
            ExpectedTdsAmount = expectedTdsAmount,
            AppliedRate = appliedRate,
            ExpectedRate = expectedRate,
            Severity = severity,
            Status = status,
            Explanation = explanation,
            EvidenceJson = evidence != null ? JsonSerializer.Serialize(evidence) : "{}",
            Jurisdiction = Jurisdiction,
            SourceReference = SourceReference,
            ReviewStatus = ReviewStatus.Pending
        };
    }
}
