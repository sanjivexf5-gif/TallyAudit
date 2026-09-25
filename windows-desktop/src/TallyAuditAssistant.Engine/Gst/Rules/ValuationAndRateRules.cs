using System.Text.Json;
using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Gst;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Gst.Rules;

// 6. Taxable value vs tax amount calculation check
public class TaxableValueVsTaxAmountRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-06";
    public override string Name => "Taxable Value vs Computed Tax Discrepancy";
    public override string Description => "Recalculates expected GST tax amounts by applying ledger tax rates to base taxable amounts and flags discrepancies exceeding the allowed tolerance.";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.0.0";
    public override string SourceReference => "CGST Act 2017 Sec 15 (Value of Taxable Supply) / Rule 35 Valuation Rules";

    public TaxableValueVsTaxAmountRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.High)
    {
        Parameters["TolerancePercentage"] = 1.0; // 1% tolerance for multi-item/freight variance
        Parameters["ToleranceRupees"] = 5.0;
    }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var tolPct = GetParam("TolerancePercentage", 1.0);
        var tolRs = (decimal)GetParam("ToleranceRupees", 5.0);
        var results = new List<GstCheckResult>();

        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName,
                   v.TotalAmount, v.PartyLedgerName,
                   l.GstRate,
                   SUM(CASE WHEN e.LedgerName LIKE '%Sales%' OR e.LedgerName LIKE '%Purchase%' OR l.ParentGroup LIKE '%Sales%' OR l.ParentGroup LIKE '%Purchase%' THEN ABS(e.Amount) ELSE 0 END) as TaxableBase,
                   SUM(CASE WHEN e.LedgerName LIKE '%GST%' OR e.LedgerName LIKE '%Tax%' THEN ABS(e.Amount) ELSE 0 END) as BookedTax
            FROM Vouchers v
            JOIN VoucherEntries e ON (e.VoucherId = v.Id)
            LEFT JOIN Ledgers l ON (l.Name = e.LedgerName AND l.CompanyId = v.CompanyId)
            WHERE v.CompanyId = @CompanyId
            GROUP BY v.Id, v.VoucherNumber, v.PartyLedgerName
            HAVING (TaxableBase > 0 AND BookedTax > 0);
        ";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var r in rows)
        {
            decimal? rateNullable = r.GstRate;
            if (!rateNullable.HasValue || rateNullable.Value <= 0)
            {
                results.Add(CreateUnableToDetermine(
                    context.CompanyId,
                    "Tax rate is not explicitly declared on master heads for voucher; unable to compute expected tax.",
                    voucherId: r.VoucherId,
                    voucherNumber: r.VoucherNumber,
                    voucherDate: r.VoucherDate,
                    voucherTypeName: r.VoucherTypeName,
                    partyName: r.PartyLedgerName
                ));
                continue;
            }

            decimal rate = rateNullable.Value;
            decimal baseAmt = r.TaxableBase;
            decimal bookedTax = r.BookedTax;
            decimal expectedTax = Math.Round(baseAmt * (rate / 100m), 2);
            decimal diff = Math.Abs(bookedTax - expectedTax);

            if (diff > tolRs && (diff / expectedTax * 100m) > (decimal)tolPct)
            {
                var exp = $"Flagged because booked tax of {bookedTax:C2} on voucher {r.VoucherNumber} deviates by {diff:C2} from expected tax of {expectedTax:C2} ({rate}% on base {baseAmt:C2}), exceeding allowable tolerance.";
                results.Add(CreateException(
                    context.CompanyId, exp, Severity,
                    voucherId: r.VoucherId,
                    voucherNumber: r.VoucherNumber,
                    voucherDate: r.VoucherDate,
                    voucherTypeName: r.VoucherTypeName,
                    partyName: r.PartyLedgerName,
                    taxableAmount: baseAmt,
                    taxAmount: bookedTax,
                    evidence: new { TaxableBase = baseAmt, AppliedRate = rate, ExpectedTax = expectedTax, BookedTax = bookedTax, Variance = diff }
                ));
            }
        }

        return results;
    }
}

// 7. GST Rate Consistency Check
public class GstRateConsistencyRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-07";
    public override string Name => "Standard Statutory GST Rate Schedule Consistency";
    public override string Description => "Validates that all configured master and voucher GST rates align with official GST Council tariff schedules (0%, 0.1%, 0.25%, 1.5%, 3%, 5%, 6%, 12%, 18%, 28%).";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.1.0";
    public override string SourceReference => "Notification No. 01/2017-Central Tax (Rate)";

    public GstRateConsistencyRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.Medium)
    {
        Parameters["StandardRates"] = "0,0.1,0.25,1.5,3,5,6,12,18,28";
    }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var rawRates = GetParam("StandardRates", "0,0.1,0.25,1.5,3,5,6,12,18,28");
        var standardRates = rawRates.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => double.TryParse(s, out var v) ? v : -1.0)
            .Where(v => v >= 0)
            .ToHashSet();

        var results = new List<GstCheckResult>();
        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT Id, Name, ParentGroup, TaxType, GstRate, HsnCode
            FROM Ledgers
            WHERE CompanyId = @CompanyId AND (TaxType = 'GST' OR GstRate IS NOT NULL);
        ";

        var ledgers = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var l in ledgers)
        {
            decimal? rate = l.GstRate;
            if (!rate.HasValue) continue;

            double val = (double)rate.Value;
            if (!standardRates.Contains(val))
            {
                var exp = $"Flagged because ledger '{l.Name}' has a non-standard GST rate of {val}%. Standard statutory tariff rates are: {rawRates}%.";
                results.Add(CreateException(
                    context.CompanyId, exp, Severity,
                    partyName: l.Name,
                    evidence: new { LedgerName = l.Name, ConfiguredRate = val, AllowedRates = rawRates }
                ));
            }
        }

        return results;
    }
}

// 12. Unusual Tax Rates (Outlier / Custom Non-standard rates)
public class UnusualTaxRatesRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-12";
    public override string Name => "Unusual or Non-Standard Tax Rate Anomaly";
    public override string Description => "Detects arbitrary or non-statutory fractional tax rates (e.g. 7.5%, 13%, 22%) applied in voucher entries.";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.0.0";
    public override string SourceReference => "GST Council Tariff Schedules";

    public UnusualTaxRatesRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.Medium)
    {
        Parameters["PermittedRates"] = "0,0.1,0.25,1.5,3,5,6,12,18,28";
    }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var raw = GetParam("PermittedRates", "0,0.1,0.25,1.5,3,5,6,12,18,28");
        var permitted = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => double.TryParse(s, out var v) ? v : -1.0)
            .Where(v => v >= 0)
            .ToHashSet();

        var results = new List<GstCheckResult>();
        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName,
                   v.TotalAmount, v.PartyLedgerName, l.Name as LedgerName, l.GstRate
            FROM Vouchers v
            JOIN VoucherEntries e ON (e.VoucherId = v.Id)
            JOIN Ledgers l ON (l.Name = e.LedgerName AND l.CompanyId = v.CompanyId)
            WHERE v.CompanyId = @CompanyId AND l.GstRate IS NOT NULL AND l.GstRate > 0;
        ";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var r in rows)
        {
            double rate = (double)r.GstRate;
            if (!permitted.Contains(rate))
            {
                var exp = $"Flagged because voucher {r.VoucherNumber} uses ledger '{r.LedgerName}' configured with an unusual tax rate of {rate}%.";
                results.Add(CreateException(
                    context.CompanyId, exp, Severity,
                    voucherId: r.VoucherId,
                    voucherNumber: r.VoucherNumber,
                    voucherDate: r.VoucherDate,
                    voucherTypeName: r.VoucherTypeName,
                    partyName: r.PartyLedgerName,
                    taxableAmount: r.TotalAmount,
                    evidence: new { VoucherNumber = r.VoucherNumber, Ledger = r.LedgerName, Rate = rate }
                ));
            }
        }

        return results;
    }
}
