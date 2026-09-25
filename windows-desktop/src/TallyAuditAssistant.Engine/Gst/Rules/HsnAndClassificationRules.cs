using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Gst;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Gst.Rules;

// 8. HSN/SAC Presence and Digit Validity
public class HsnSacPresenceRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-08";
    public override string Name => "HSN / SAC Code Presence and Length Validity";
    public override string Description => "Ensures that all taxable supply and procurement ledger heads carry a valid 4, 6, or 8 digit HSN (Goods) or SAC (Services) tariff code.";
    public override DateTime EffectiveDate => new DateTime(2021, 4, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.2.0";
    public override string SourceReference => "Notification No. 78/2020-Central Tax / Rule 46(d) Tax Invoice";

    public HsnSacPresenceRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.Medium)
    {
        Parameters["MinimumDigits"] = 4;
        Parameters["ExemptThresholdTurnover"] = false;
    }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var minDigits = GetParam("MinimumDigits", 4);
        var results = new List<GstCheckResult>();

        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT Id, Name, ParentGroup, TaxType, GstRate, HsnCode
            FROM Ledgers
            WHERE CompanyId = @CompanyId 
              AND (TaxType = 'GST' OR GstRate > 0 OR ParentGroup IN ('Sales Accounts', 'Purchase Accounts', 'Direct Expenses', 'Direct Incomes'));
        ";

        var ledgers = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var l in ledgers)
        {
            string? code = (string?)l.HsnCode;
            if (string.IsNullOrWhiteSpace(code))
            {
                var exp = $"Flagged because taxable master head '{l.Name}' (Parent: {l.ParentGroup}, Rate: {l.GstRate ?? 0}%) has no HSN/SAC tariff classification code specified.";
                results.Add(CreateException(
                    context.CompanyId, exp, Severity,
                    partyName: l.Name,
                    evidence: new { Ledger = l.Name, ParentGroup = l.ParentGroup, Rate = l.GstRate, HSN = "Missing" }
                ));
            }
            else
            {
                string clean = code.Trim();
                if (clean.Length < minDigits || !clean.All(char.IsDigit))
                {
                    var exp = $"Flagged because HSN/SAC code '{clean}' for ledger '{l.Name}' has only {clean.Length} digits, which is less than the statutory minimum of {minDigits} digits.";
                    results.Add(CreateException(
                        context.CompanyId, exp, SeverityLevel.Low,
                        partyName: l.Name,
                        evidence: new { Ledger = l.Name, RecordedCode = clean, Length = clean.Length, RequiredMin = minDigits }
                    ));
                }
            }
        }

        return results;
    }
}
