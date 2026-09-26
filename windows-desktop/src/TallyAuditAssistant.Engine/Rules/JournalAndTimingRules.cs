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
            decimal amt = GetDecimal(e, "Amount");
            string? vNum = GetString(e, "VoucherNumber");
            string? vId = GetString(e, "Id");
            DateTime? vDate = GetDateTime(e, "VoucherDate");
            string lName = GetString(e, "LedgerName") ?? string.Empty;

            var explanation = $"Flagged because journal entry {vNum} moves liquidity through '{lName}' ({amt:C2}) instead of using a standard Payment, Receipt, or Contra voucher type.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: vId,
                voucherNumber: vNum,
                voucherDate: vDate,
                flaggedAmount: amt,
                evidenceObj: new { VoucherNumber = vNum, LiquidityLedger = lName, Amount = amt }
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
        Parameters["HighValueThreshold"] = 500000.0m;
    }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var threshold = GetParam("HighValueThreshold", 500000.0m);
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
            decimal totalAmount = GetDecimal(j, "TotalAmount");
            string? vNum = GetString(j, "VoucherNumber");
            string? vId = GetString(j, "Id");
            DateTime? vDate = GetDateTime(j, "VoucherDate");
            string? narration = GetString(j, "Narration");

            var explanation = $"Flagged because this journal amount of {totalAmount:C2} is significantly higher than the standard review threshold of {threshold:C2} for manual journal entries.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: vId,
                voucherNumber: vNum,
                voucherDate: vDate,
                flaggedAmount: totalAmount,
                evidenceObj: new { VoucherNumber = vNum, Amount = totalAmount, Threshold = threshold, Narration = narration }
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
            DateTime? vDate = GetDateTime(v, "VoucherDate");
            DateTime? bDate = GetDateTime(v, "BooksFromDate");
            decimal totalAmount = GetDecimal(v, "TotalAmount");
            string? vNum = GetString(v, "VoucherNumber");
            string? vId = GetString(v, "Id");

            var explanation = $"Flagged because voucher {vNum} has date {vDate:dd-MMM-yyyy}, which precedes the company books commencement date ({bDate:dd-MMM-yyyy}).";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: vId,
                voucherNumber: vNum,
                voucherDate: vDate,
                flaggedAmount: totalAmount,
                evidenceObj: new { VoucherNumber = vNum, VoucherDate = vDate, BooksBeginningDate = bDate }
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
        Parameters["YearEndThreshold"] = 100000.0m;
    }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var threshold = GetParam("YearEndThreshold", 100000.0m);
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
            decimal totalAmount = GetDecimal(a, "TotalAmount");
            string? vNum = GetString(a, "VoucherNumber");
            string? vId = GetString(a, "Id");
            DateTime? vDate = GetDateTime(a, "VoucherDate");
            string? narration = GetString(a, "Narration");

            var explanation = $"Flagged because this closing journal voucher of {totalAmount:C2} was posted on year-end date (31-March) requiring period-end cut-off review.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: vId,
                voucherNumber: vNum,
                voucherDate: vDate,
                flaggedAmount: totalAmount,
                evidenceObj: new { VoucherNumber = vNum, VoucherDate = vDate, Amount = totalAmount, Narration = narration }
            ));
        }

        return results;
    }
}
