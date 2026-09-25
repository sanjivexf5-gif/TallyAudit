using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Rules;

// 11. Voucher Numbering Gap Rule
public class VoucherNumberingGapRule : BaseAuditRule
{
    public override string RuleId => "ACC-SEQ-01";
    public override string Name => "Voucher Sequence Gap";
    public override RuleCategory Category => RuleCategory.VoucherSequencing;
    public override string Description => "Identifies skipped or missing numbers in sequential numerical voucher numbering series.";

    public VoucherNumberingGapRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Medium) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT VoucherTypeName, CAST(VoucherNumber AS INTEGER) as Num, VoucherDate
            FROM Vouchers
            WHERE CompanyId = @CompanyId 
              AND VoucherNumber GLOB '[0-9]*'
              AND CAST(VoucherNumber AS INTEGER) > 0
            ORDER BY VoucherTypeName, Num ASC;
        ";

        var rows = (await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken))).ToList();

        var grouped = rows.GroupBy(r => (string)r.VoucherTypeName);
        foreach (var group in grouped)
        {
            var ordered = group.OrderBy(x => (long)x.Num).ToList();
            for (int i = 0; i < ordered.Count - 1; i++)
            {
                long current = ordered[i].Num;
                long next = ordered[i + 1].Num;
                if (next > current + 1 && next <= current + 10) // Small contiguous gap
                {
                    var explanation = $"Flagged because a numbering sequence gap was detected in '{group.Key}' between voucher number {current} and {next} (Missing: {current + 1} to {next - 1}).";
                    results.Add(CreateResult(
                        context.CompanyId,
                        explanation,
                        Severity,
                        voucherNumber: current.ToString(),
                        evidenceObj: new { VoucherType = group.Key, PreviousNumber = current, NextNumber = next, GapSize = next - current - 1 }
                    ));
                }
            }
        }

        return results;
    }
}

// 12. Unusual Transaction Amount Rule
public class UnusualTransactionAmountRule : BaseAuditRule
{
    public override string RuleId => "ACC-ANO-01";
    public override string Name => "Statistical Transaction Amount Outlier";
    public override RuleCategory Category => RuleCategory.AnomalyDetection;
    public override string Description => "Flagged because this transaction amount is significantly higher than the standard deviation and average for its voucher category.";

    public UnusualTransactionAmountRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Medium)
    {
        Parameters["OutlierMultiplier"] = 3.5;
    }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var multiplier = GetParam("OutlierMultiplier", 3.5);
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string statSql = @"
            SELECT VoucherTypeName, AVG(TotalAmount) as AvgAmount, MAX(TotalAmount) as MaxAmount
            FROM Vouchers
            WHERE CompanyId = @CompanyId AND TotalAmount > 0
            GROUP BY VoucherTypeName;
        ";

        var stats = (await connection.QueryAsync(new CommandDefinition(statSql, new { context.CompanyId }, cancellationToken: cancellationToken))).ToList();

        foreach (var s in stats)
        {
            string vType = s.VoucherTypeName;
            decimal avg = s.AvgAmount;
            decimal threshold = avg * (decimal)multiplier;

            const string outlierSql = @"
                SELECT Id, VoucherTypeName, VoucherNumber, VoucherDate, TotalAmount, PartyLedgerName
                FROM Vouchers
                WHERE CompanyId = @CompanyId AND VoucherTypeName = @VType AND TotalAmount >= @Threshold;
            ";

            var outliers = await connection.QueryAsync(new CommandDefinition(outlierSql, new { context.CompanyId, VType = vType, Threshold = threshold }, cancellationToken: cancellationToken));

            foreach (var o in outliers)
            {
                var explanation = $"Flagged because this {vType} voucher amount of {o.TotalAmount:C2} is more than {multiplier}x the category average ({avg:C2}).";
                results.Add(CreateResult(
                    context.CompanyId,
                    explanation,
                    Severity,
                    voucherId: o.Id,
                    voucherNumber: o.VoucherNumber,
                    voucherDate: o.VoucherDate,
                    flaggedAmount: o.TotalAmount,
                    evidenceObj: new { VoucherType = vType, Amount = o.TotalAmount, CategoryAverage = avg, Multiplier = multiplier }
                ));
            }
        }

        return results;
    }
}

// 13. Round-Number Transaction Pattern Rule
public class RoundNumberPatternRule : BaseAuditRule
{
    public override string RuleId => "ACC-ANO-02";
    public override string Name => "High-Value Exact Round Number Payment";
    public override RuleCategory Category => RuleCategory.AnomalyDetection;
    public override string Description => "Identifies disbursements in exact thousands (multiples of ₹10,000 or ₹50,000) that may represent unitemized advances or estimated payments.";

    public RoundNumberPatternRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Low)
    {
        Parameters["RoundMultiple"] = 10000;
        Parameters["MinimumAmount"] = 50000.0;
    }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var multiple = GetParam("RoundMultiple", 10000);
        var minAmount = GetParam("MinimumAmount", 50000.0);
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT Id, VoucherTypeName, VoucherNumber, VoucherDate, TotalAmount, PartyLedgerName, Narration
            FROM Vouchers
            WHERE CompanyId = @CompanyId 
              AND (VoucherTypeName LIKE '%Payment%' OR VoucherTypeName LIKE '%Journal%')
              AND TotalAmount >= @MinAmount
              AND CAST(TotalAmount AS INTEGER) % @Multiple = 0
              AND TotalAmount = CAST(TotalAmount AS INTEGER);
        ";

        var vouchers = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, MinAmount = minAmount, Multiple = multiple }, cancellationToken: cancellationToken));

        foreach (var v in vouchers)
        {
            var explanation = $"Flagged because payment voucher {v.VoucherNumber} has an exact round figure of {v.TotalAmount:C2} (multiple of {multiple:C0}) without fractional paise.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: v.Id,
                voucherNumber: v.VoucherNumber,
                voucherDate: v.VoucherDate,
                flaggedAmount: v.TotalAmount,
                evidenceObj: new { v.VoucherNumber, Amount = v.TotalAmount, Party = v.PartyLedgerName, RoundMultiple = multiple }
            ));
        }

        return results;
    }
}

// 14. Unusual Ledger Combination Rule
public class UnusualLedgerCombinationRule : BaseAuditRule
{
    public override string RuleId => "ACC-LGD-01";
    public override string Name => "Direct Capital to Expense Posting";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Detects entries directly crediting/debiting Capital or Reserves accounts alongside direct operating expense accounts.";

    public UnusualLedgerCombinationRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.High) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id, v.VoucherTypeName, v.VoucherNumber, v.VoucherDate, v.TotalAmount
            FROM Vouchers v
            WHERE v.CompanyId = @CompanyId 
              AND EXISTS (
                  SELECT 1 FROM VoucherEntries e1 
                  JOIN Ledgers l1 ON (l1.CompanyId = v.CompanyId AND l1.Name = e1.LedgerName)
                  WHERE e1.VoucherId = v.Id AND l1.ParentGroup IN ('Capital Account', 'Reserves & Surplus')
              )
              AND EXISTS (
                  SELECT 1 FROM VoucherEntries e2 
                  JOIN Ledgers l2 ON (l2.CompanyId = v.CompanyId AND l2.Name = e2.LedgerName)
                  WHERE e2.VoucherId = v.Id AND l2.ParentGroup IN ('Indirect Expenses', 'Direct Expenses')
              );
        ";

        var vouchers = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var v in vouchers)
        {
            var explanation = $"Flagged because voucher {v.VoucherNumber} directly combines equity/capital accounts with operating expense allocations in a single entry.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: v.Id,
                voucherNumber: v.VoucherNumber,
                voucherDate: v.VoucherDate,
                flaggedAmount: v.TotalAmount,
                evidenceObj: new { v.VoucherNumber, v.VoucherTypeName, Amount = v.TotalAmount }
            ));
        }

        return results;
    }
}

// 15. Reversal Anomaly Rule
public class ReversalAnomalyRule : BaseAuditRule
{
    public override string RuleId => "ACC-REV-01";
    public override string Name => "Delayed Transaction Reversal";
    public override RuleCategory Category => RuleCategory.AnomalyDetection;
    public override string Description => "Detects reversal or cancellation entries occurring more than 90 days after the original voucher transaction date.";

    public ReversalAnomalyRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Medium)
    {
        Parameters["ReversalThresholdDays"] = 90;
    }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var maxDays = GetParam("ReversalThresholdDays", 90);
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v2.Id, v2.VoucherNumber, v2.VoucherDate as ReversalDate, v1.VoucherDate as OriginalDate, 
                   CAST(julianday(v2.VoucherDate) - julianday(v1.VoucherDate) AS INTEGER) as DaysBetween,
                   v2.TotalAmount, v2.PartyLedgerName
            FROM Vouchers v2
            JOIN Vouchers v1 ON (v1.CompanyId = v2.CompanyId AND v1.PartyLedgerName = v2.PartyLedgerName AND v1.TotalAmount = v2.TotalAmount)
            WHERE v2.CompanyId = @CompanyId 
              AND v2.VoucherTypeName LIKE '%Credit Note%' 
              AND v1.VoucherTypeName LIKE '%Sales%'
              AND (julianday(v2.VoucherDate) - julianday(v1.VoucherDate)) > @MaxDays;
        ";

        var reversals = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, MaxDays = maxDays }, cancellationToken: cancellationToken));

        foreach (var r in reversals)
        {
            long days = r.DaysBetween;
            var explanation = $"Flagged because credit note {r.VoucherNumber} reverses invoice dated {r.OriginalDate:dd-MMM-yyyy} after {days} days, exceeding the normal {maxDays}-day reversal window.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: r.Id,
                voucherNumber: r.VoucherNumber,
                voucherDate: r.ReversalDate,
                flaggedAmount: r.TotalAmount,
                evidenceObj: new { r.VoucherNumber, DaysDelayed = days, ThresholdDays = maxDays }
            ));
        }

        return results;
    }
}

// 16. Credit/Debit Note Anomaly Rule
public class CreditDebitNoteAnomalyRule : BaseAuditRule
{
    public override string RuleId => "ACC-CDN-01";
    public override string Name => "Credit or Debit Note Missing Original Invoice Reference";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Detects Credit Notes or Debit Notes issued without linking to an original sales/purchase invoice reference number.";

    public CreditDebitNoteAnomalyRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Medium) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT Id, VoucherTypeName, VoucherNumber, VoucherDate, TotalAmount, PartyLedgerName
            FROM Vouchers
            WHERE CompanyId = @CompanyId 
              AND (VoucherTypeName LIKE '%Credit Note%' OR VoucherTypeName LIKE '%Debit Note%')
              AND (ReferenceNumber IS NULL OR TRIM(ReferenceNumber) = '');
        ";

        var notes = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var n in notes)
        {
            var explanation = $"Flagged because {n.VoucherTypeName} {n.VoucherNumber} has no original invoice reference number recorded for statutory tracking.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: n.Id,
                voucherNumber: n.VoucherNumber,
                voucherDate: n.VoucherDate,
                flaggedAmount: n.TotalAmount,
                evidenceObj: new { n.VoucherTypeName, n.VoucherNumber, Amount = n.TotalAmount, Party = n.PartyLedgerName }
            ));
        }

        return results;
    }
}
