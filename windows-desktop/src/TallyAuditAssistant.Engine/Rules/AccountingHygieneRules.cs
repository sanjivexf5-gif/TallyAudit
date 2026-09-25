using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Rules;

// 3. Missing Narration Rule
public class MissingNarrationRule : BaseAuditRule
{
    public override string RuleId => "ACC-NAR-01";
    public override string Name => "Missing Transaction Narration";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Identifies vouchers exceeding a value threshold where the narration field is left empty or blank.";

    public MissingNarrationRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Low)
    {
        Parameters["MinimumAmountThreshold"] = 10000.0;
    }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var minAmount = GetParam("MinimumAmountThreshold", 10000.0);
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT Id, VoucherTypeName, VoucherNumber, VoucherDate, TotalAmount, PartyLedgerName
            FROM Vouchers
            WHERE CompanyId = @CompanyId 
              AND (Narration IS NULL OR TRIM(Narration) = '' OR LENGTH(TRIM(Narration)) < 3)
              AND TotalAmount >= @MinAmount;
        ";

        var vouchers = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, MinAmount = minAmount }, cancellationToken: cancellationToken));

        foreach (var v in vouchers)
        {
            var explanation = $"Flagged because this {v.VoucherTypeName} of {v.TotalAmount:C2} has no descriptive narration recorded.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: v.Id,
                voucherNumber: v.VoucherNumber,
                voucherDate: v.VoucherDate,
                flaggedAmount: v.TotalAmount,
                evidenceObj: new { v.VoucherTypeName, v.VoucherNumber, Amount = v.TotalAmount, Party = v.PartyLedgerName }
            ));
        }

        return results;
    }
}

// 4. Missing Party Information Rule
public class MissingPartyInfoRule : BaseAuditRule
{
    public override string RuleId => "ACC-PTY-01";
    public override string Name => "Missing Party Identification on Commercial Voucher";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Detects Purchase or Sales vouchers where the party ledger name is missing or undefined.";

    public MissingPartyInfoRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Medium) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT Id, VoucherTypeName, VoucherNumber, VoucherDate, TotalAmount
            FROM Vouchers
            WHERE CompanyId = @CompanyId 
              AND (VoucherTypeName LIKE '%Purchase%' OR VoucherTypeName LIKE '%Sales%')
              AND (PartyLedgerName IS NULL OR TRIM(PartyLedgerName) = '' OR PartyLedgerName = 'Cash');
        ";

        var vouchers = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var v in vouchers)
        {
            var explanation = $"Flagged because commercial voucher {v.VoucherNumber} ({v.VoucherTypeName}) lacks an identifiable named party or customer ledger.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: v.Id,
                voucherNumber: v.VoucherNumber,
                voucherDate: v.VoucherDate,
                flaggedAmount: v.TotalAmount,
                evidenceObj: new { v.VoucherTypeName, v.VoucherNumber, Amount = v.TotalAmount }
            ));
        }

        return results;
    }
}

// 5. Negative Ledger Balance Rule
public class NegativeLedgerBalanceRule : BaseAuditRule
{
    public override string RuleId => "ACC-BAL-01";
    public override string Name => "Negative Cash-in-Hand Balance";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Identifies cash-in-hand accounts whose closing balance is negative.";

    public NegativeLedgerBalanceRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.High) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT Id, Name, ParentGroup, ClosingBalance
            FROM Ledgers
            WHERE CompanyId = @CompanyId 
              AND (ParentGroup = 'Cash-in-Hand' OR Name LIKE '%Cash%')
              AND ClosingBalance < 0;
        ";

        var ledgers = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var l in ledgers)
        {
            decimal clBal = l.ClosingBalance;
            var explanation = $"Flagged because cash ledger '{l.Name}' reflects a negative closing balance of {clBal:C2}.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                ledgerId: l.Id,
                flaggedAmount: clBal,
                evidenceObj: new { LedgerName = l.Name, l.ParentGroup, NegativeClosingBalance = clBal }
            ));
        }

        return results;
    }
}

// 6. Suspense Ledger Activity Rule
public class SuspenseLedgerActivityRule : BaseAuditRule
{
    public override string RuleId => "ACC-SUS-01";
    public override string Name => "Direct Suspense Account Posting";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Detects transactions debited or credited directly into Suspense or unclassified clearing accounts.";

    public SuspenseLedgerActivityRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Medium)
    {
        Parameters["SuspenseToleranceLimit"] = 5000.0;
    }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var tolerance = GetParam("SuspenseToleranceLimit", 5000.0);
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id, v.VoucherTypeName, v.VoucherNumber, v.VoucherDate, e.LedgerName, e.Amount
            FROM VoucherEntries e
            JOIN Vouchers v ON e.VoucherId = v.Id
            WHERE v.CompanyId = @CompanyId 
              AND (e.LedgerName LIKE '%Suspense%' OR e.LedgerName LIKE '%Rounding Off%')
              AND ABS(e.Amount) >= @Tolerance;
        ";

        var entries = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, Tolerance = tolerance }, cancellationToken: cancellationToken));

        foreach (var e in entries)
        {
            decimal amt = e.Amount;
            var explanation = $"Flagged because voucher {e.VoucherNumber} allocates {amt:C2} directly to '{e.LedgerName}', exceeding the review tolerance of {tolerance:C2}.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: e.Id,
                voucherNumber: e.VoucherNumber,
                voucherDate: e.VoucherDate,
                flaggedAmount: amt,
                evidenceObj: new { e.VoucherNumber, SuspenseLedger = e.LedgerName, AllocatedAmount = amt, Tolerance = tolerance }
            ));
        }

        return results;
    }
}
