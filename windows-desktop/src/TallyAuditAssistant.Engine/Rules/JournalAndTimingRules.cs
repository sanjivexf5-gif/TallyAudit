using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Rules;

// 7. Unusual Journal Entry Rule
public class UnusualJournalEntryRule : BaseAuditRule
{
    public override string RuleId => "ACC-JRN-01";
    public override string Name => "Cash or Bank Movement in Journal Voucher";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Detects journal vouchers that affect cash or bank accounts directly rather than using standard payment, receipt, or contra voucher types.";

    public UnusualJournalEntryRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Medium) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id, v.VoucherTypeName, v.VoucherNumber, v.VoucherDate, e.LedgerName, e.Amount
            FROM VoucherEntries e
            JOIN Vouchers v ON e.VoucherId = v.Id
            JOIN Ledgers l ON (l.CompanyId = v.CompanyId AND l.Name = e.LedgerName)
            WHERE v.CompanyId = @CompanyId 
              AND v.VoucherTypeName LIKE '%Journal%'
              AND (l.ParentGroup IN ('Cash-in-Hand', 'Bank Accounts', 'Bank OD A/c') OR e.LedgerName LIKE '%Cash%' OR e.LedgerName LIKE '%Bank%');
        ";

        var entries = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var e in entries)
        {
            var explanation = $"Flagged because journal entry {e.VoucherNumber} moves liquidity through '{e.LedgerName}' ({e.Amount:C2}) instead of using a standard Payment, Receipt, or Contra voucher type.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: e.Id,
                voucherNumber: e.VoucherNumber,
                voucherDate: e.VoucherDate,
                flaggedAmount: e.Amount,
                evidenceObj: new { e.VoucherNumber, LiquidityLedger = e.LedgerName, e.Amount }
            ));
        }

        return results;
    }
}

// 8. Large Manual Journal Rule
public class LargeManualJournalRule : BaseAuditRule
{
    public override string RuleId => "ACC-JRN-02";
    public override string Name => "Unusually High-Value Manual Journal";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Flagged because this journal amount is significantly higher than the median value of similar journals during the selected period.";

    public LargeManualJournalRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.High)
    {
        Parameters["HighValueThreshold"] = 500000.0;
    }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var threshold = GetParam("HighValueThreshold", 500000.0);
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT Id, VoucherTypeName, VoucherNumber, VoucherDate, TotalAmount, Narration
            FROM Vouchers
            WHERE CompanyId = @CompanyId 
              AND VoucherTypeName LIKE '%Journal%'
              AND TotalAmount >= @Threshold;
        ";

        var journals = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, Threshold = threshold }, cancellationToken: cancellationToken));

        foreach (var j in journals)
        {
            var explanation = $"Flagged because this journal amount of {j.TotalAmount:C2} is significantly higher than the standard review threshold of {threshold:C2} for manual journal entries.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: j.Id,
                voucherNumber: j.VoucherNumber,
                voucherDate: j.VoucherDate,
                flaggedAmount: j.TotalAmount,
                evidenceObj: new { j.VoucherNumber, Amount = j.TotalAmount, Threshold = threshold, j.Narration }
            ));
        }

        return results;
    }
}

// 9. Backdated Transaction Rule
public class BackdatedTransactionRule : BaseAuditRule
{
    public override string RuleId => "ACC-TIM-01";
    public override string Name => "Backdated Transaction Posting";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Identifies vouchers recorded with transaction dates prior to the company's books beginning date.";

    public BackdatedTransactionRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Medium) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id, v.VoucherTypeName, v.VoucherNumber, v.VoucherDate, v.TotalAmount, c.BooksFromDate
            FROM Vouchers v
            JOIN Companies c ON v.CompanyId = c.Id
            WHERE v.CompanyId = @CompanyId 
              AND v.VoucherDate < c.BooksFromDate;
        ";

        var vouchers = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var v in vouchers)
        {
            DateTime vDate = v.VoucherDate;
            DateTime bDate = v.BooksFromDate;
            var explanation = $"Flagged because voucher {v.VoucherNumber} has date {vDate:dd-MMM-yyyy}, which precedes the company books commencement date ({bDate:dd-MMM-yyyy}).";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: v.Id,
                voucherNumber: v.VoucherNumber,
                voucherDate: vDate,
                flaggedAmount: v.TotalAmount,
                evidenceObj: new { v.VoucherNumber, VoucherDate = vDate, BooksBeginningDate = bDate }
            ));
        }

        return results;
    }
}

// 10. Year-End Adjustment Rule
public class YearEndAdjustmentRule : BaseAuditRule
{
    public override string RuleId => "ACC-TIM-02";
    public override string Name => "High-Value Year-End Closing Adjustment";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Identifies manual adjustment entries dated on March 31st (financial year closing date) exceeding materiality threshold.";

    public YearEndAdjustmentRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Medium)
    {
        Parameters["YearEndThreshold"] = 100000.0;
    }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var threshold = GetParam("YearEndThreshold", 100000.0);
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT Id, VoucherTypeName, VoucherNumber, VoucherDate, TotalAmount, Narration
            FROM Vouchers
            WHERE CompanyId = @CompanyId 
              AND strftime('%m-%d', VoucherDate) = '03-31'
              AND VoucherTypeName LIKE '%Journal%'
              AND TotalAmount >= @Threshold;
        ";

        var adjustments = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, Threshold = threshold }, cancellationToken: cancellationToken));

        foreach (var a in adjustments)
        {
            var explanation = $"Flagged because this closing journal voucher of {a.TotalAmount:C2} was posted on year-end date (31-March) requiring period-end cut-off review.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: a.Id,
                voucherNumber: a.VoucherNumber,
                voucherDate: a.VoucherDate,
                flaggedAmount: a.TotalAmount,
                evidenceObj: new { a.VoucherNumber, a.VoucherDate, Amount = a.TotalAmount, a.Narration }
            ));
        }

        return results;
    }
}
