using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Tds;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Tds.Rules;

// 10. Missing TDS Entries on High-Value Commercial Vouchers Rule
public class MissingTdsEntriesRule : BaseTdsRule
{
    public override string RuleId => "TDS-CHK-10";
    public override string Name => "Missing Withholding Tax Entries on High-Value Contractor / Service Bills";
    public override string Section => "194C / 194J High-Value";
    public override string Description => "Flags high-value service, subcontracting, and professional invoices (e.g. > ₹1,00,000) entered into accounts with 0% TDS line items.";
    public override string SourceReference => "Income Tax Act 1961 Section 194C & Section 194J Mandatory Deduction";
    public override SeverityLevel Severity { get; set; } = SeverityLevel.High;

    public MissingTdsEntriesRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory)
    {
        Parameters = new Dictionary<string, object>
        {
            { "HighValueThreshold", 100000m }
        };
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var threshold = GetParam("HighValueThreshold", 100000m);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName, v.TotalAmount, v.PartyLedgerName,
                   e.LedgerName as ExpenseHead, e.Amount as ExpenseAmount, l.PAN as PartyPan
            FROM Vouchers v
            JOIN VoucherEntries e ON (e.VoucherId = v.Id AND e.IsDebit = 1)
            LEFT JOIN Ledgers l ON (l.CompanyId = v.CompanyId AND l.Name = v.PartyLedgerName)
            WHERE v.CompanyId = @CompanyId AND v.VoucherTypeName IN ('Purchase', 'Journal', 'Payment')
              AND e.Amount >= @Threshold
              AND (e.LedgerName LIKE '%Contract%' OR e.LedgerName LIKE '%Legal%' OR e.LedgerName LIKE '%Professional%' OR e.LedgerName LIKE '%Consult%' OR e.LedgerName LIKE '%Technical%' OR e.LedgerName LIKE '%Fabrication%');
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, Threshold = threshold }, cancellationToken: cancellationToken));
        var results = new List<TdsCheckResult>();

        foreach (var r in rows)
        {
            const string checkTdsSql = @"
                SELECT COUNT(*) FROM VoucherEntries
                WHERE VoucherId = @VoucherId AND (LedgerName LIKE '%TDS%' OR LedgerName LIKE '%Tax Deducted%');
            ";
            int count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(checkTdsSql, new { r.VoucherId }, cancellationToken: cancellationToken));

            if (count == 0)
            {
                results.Add(CreateResult(
                    context.CompanyId,
                    "High-value service invoice " + (string)r.VoucherNumber + " (" + ((decimal)r.ExpenseAmount).ToString("C2") + " on head '" + (string)r.ExpenseHead + "') lacks an accompanying TDS withholding tax line item.",
                    Severity,
                    TdsCheckStatus.Exception,
                    voucherId: (string)r.VoucherId,
                    voucherNumber: (string)r.VoucherNumber,
                    voucherDate: GetDateTime(r, "VoucherDate"),
                    voucherTypeName: (string)r.VoucherTypeName,
                    partyLedgerName: (string?)r.PartyLedgerName,
                    partyPan: (string?)r.PartyPan,
                    expenseLedgerName: (string)r.ExpenseHead,
                    transactionAmount: (decimal)r.ExpenseAmount,
                    sectionThreshold: threshold,
                    evidence: new { Voucher = (string)r.VoucherNumber, Amount = (decimal)r.ExpenseAmount, ExpenseHead = (string)r.ExpenseHead }
                ));
            }
        }

        return results;
    }
}

// 11. Unusual or Non-Statutory Fractional TDS Rates Rule
public class UnusualTdsRatesRule : BaseTdsRule
{
    public override string RuleId => "TDS-CHK-11";
    public override string Name => "Unusual or Non-Statutory TDS Deduction Rate Pattern";
    public override string Section => "Statutory Tariff Schedule";
    public override string Description => "Detects arbitrary or non-statutory fractional TDS rates applied to invoices (standard statutory rates: 0.1%, 1%, 2%, 5%, 10%, 20%, 30%).";
    public override string SourceReference => "Income Tax Act 1961 Chapter XVII-B Statutory Rate Schedule";
    public override SeverityLevel Severity { get; set; } = SeverityLevel.Medium;

    public UnusualTdsRatesRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory)
    {
        Parameters = new Dictionary<string, object>
        {
            { "ValidRates", "0.1,1,2,3.75,5,7.5,10,20,30" }
        };
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var validRatesStr = GetParam("ValidRates", "0.1,1,2,3.75,5,7.5,10,20,30");
        var validRates = validRatesStr.Split(',').Select(s => decimal.TryParse(s.Trim(), out var v) ? v : -1).Where(v => v >= 0).ToHashSet();

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName, v.PartyLedgerName,
                   exp.Amount as BaseAmount, ABS(tds.Amount) as TdsAmount, tds.LedgerName as TdsHead, l.PAN as PartyPan
            FROM Vouchers v
            JOIN VoucherEntries exp ON (exp.VoucherId = v.Id AND exp.IsDebit = 1)
            JOIN VoucherEntries tds ON (tds.VoucherId = v.Id AND (tds.LedgerName LIKE '%TDS%' OR tds.LedgerName LIKE '%Tax Deducted%'))
            LEFT JOIN Ledgers l ON (l.CompanyId = v.CompanyId AND l.Name = v.PartyLedgerName)
            WHERE v.CompanyId = @CompanyId AND exp.Amount > 0 AND ABS(tds.Amount) > 0;
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));
        var results = new List<TdsCheckResult>();

        foreach (var r in rows)
        {
            decimal baseAmt = (decimal)r.BaseAmount;
            decimal tdsAmt = (decimal)r.TdsAmount;
            decimal effectiveRate = Math.Round((tdsAmt / baseAmt) * 100m, 2);

            bool matchesStandard = validRates.Any(vr => Math.Abs(vr - effectiveRate) <= 0.05m);

            if (!matchesStandard && effectiveRate > 0 && effectiveRate < 100)
            {
                results.Add(CreateResult(
                    context.CompanyId,
                    "Unusual withholding tax rate detected in voucher " + (string)r.VoucherNumber + ": Effective deduction rate is " + effectiveRate + "% (TDS: " + tdsAmt.ToString("C2") + " on Base: " + baseAmt.ToString("C2") + "), which does not correspond to standard statutory rates (0.1%, 1%, 2%, 5%, 10%, 20%).",
                    Severity,
                    TdsCheckStatus.Exception,
                    voucherId: (string)r.VoucherId,
                    voucherNumber: (string)r.VoucherNumber,
                    voucherDate: GetDateTime(r, "VoucherDate"),
                    voucherTypeName: (string)r.VoucherTypeName,
                    partyLedgerName: (string?)r.PartyLedgerName,
                    partyPan: (string?)r.PartyPan,
                    transactionAmount: baseAmt,
                    deductedTdsAmount: tdsAmt,
                    appliedRate: effectiveRate,
                    evidence: new { BaseAmount = baseAmt, TdsAmount = tdsAmt, CalculatedRate = effectiveRate, Voucher = (string)r.VoucherNumber }
                ));
            }
        }

        return results;
    }
}

// 12. TDS Reversal Anomalies Rule
public class TdsReversalAnomaliesRule : BaseTdsRule
{
    public override string RuleId => "TDS-CHK-12";
    public override string Name => "TDS Debit Reversal & Manual Tax Adjustment Anomaly";
    public override string Section => "Journal Reversals";
    public override string Description => "Detects debit entries or manual credit reversals posted into TDS liability ledgers outside of official government challan payment vouchers.";
    public override string SourceReference => "Income Tax Rules 1962 / Tax Audit Guidance Note";
    public override SeverityLevel Severity { get; set; } = SeverityLevel.High;

    public TdsReversalAnomaliesRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName, v.Narration,
                   e.LedgerName, e.Amount as ReversalAmount
            FROM Vouchers v
            JOIN VoucherEntries e ON (e.VoucherId = v.Id)
            WHERE v.CompanyId = @CompanyId
              AND (e.LedgerName LIKE '%TDS%' OR e.LedgerName LIKE '%Tax Deducted%')
              AND e.IsDebit = 1
              AND v.VoucherTypeName NOT IN ('Payment', 'Bank Payment', 'Challan Payment');
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));
        var results = new List<TdsCheckResult>();

        foreach (var r in rows)
        {
            results.Add(CreateResult(
                context.CompanyId,
                "Irregular TDS liability reduction posted via non-payment voucher " + (string)r.VoucherNumber + " (" + (string)r.VoucherTypeName + ") of amount " + ((decimal)r.ReversalAmount).ToString("C2") + " on ledger '" + (string)r.LedgerName + "'. TDS liabilities should only be discharged through statutory government challans.",
                Severity,
                TdsCheckStatus.Exception,
                voucherId: (string)r.VoucherId,
                voucherNumber: (string)r.VoucherNumber,
                voucherDate: GetDateTime(r, "VoucherDate"),
                voucherTypeName: (string)r.VoucherTypeName,
                partyLedgerName: (string)r.LedgerName,
                transactionAmount: (decimal)r.ReversalAmount,
                evidence: new { VoucherType = (string)r.VoucherTypeName, VoucherNumber = (string)r.VoucherNumber, ReversalAmount = (decimal)r.ReversalAmount, Narration = (string?)r.Narration }
            ));
        }

        return results;
    }
}
