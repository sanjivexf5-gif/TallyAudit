using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Rules;

// 17. Missing GST Information Rule
public class MissingGstInformationRule : BaseAuditRule
{
    public override string RuleId => "GST-MISS-01";
    public override string Name => "B2B Transaction with Missing GSTIN";
    public override RuleCategory Category => RuleCategory.GST;
    public override string Description => "Identifies high-value B2B purchase or sales transactions recorded against party ledgers where the GSTIN is missing.";

    public MissingGstInformationRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.High)
    {
        Parameters["B2BThreshold"] = 50000.0;
    }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var threshold = GetParam("B2BThreshold", 50000.0);
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id, v.VoucherTypeName, v.VoucherNumber, v.VoucherDate, v.TotalAmount, v.PartyLedgerName, l.GSTIN
            FROM Vouchers v
            JOIN Ledgers l ON (l.CompanyId = v.CompanyId AND l.Name = v.PartyLedgerName)
            WHERE v.CompanyId = @CompanyId 
              AND (v.VoucherTypeName LIKE '%Purchase%' OR v.VoucherTypeName LIKE '%Sales%')
              AND v.TotalAmount >= @Threshold
              AND (l.GSTIN IS NULL OR TRIM(l.GSTIN) = '' OR LENGTH(TRIM(l.GSTIN)) != 15);
        ";

        var vouchers = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, Threshold = threshold }, cancellationToken: cancellationToken));

        foreach (var v in vouchers)
        {
            var explanation = $"Flagged because B2B transaction {v.VoucherNumber} of {v.TotalAmount:C2} is recorded against '{v.PartyLedgerName}' which lacks a valid 15-character GSTIN.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: v.Id,
                voucherNumber: v.VoucherNumber,
                voucherDate: v.VoucherDate,
                flaggedAmount: v.TotalAmount,
                evidenceObj: new { v.VoucherNumber, Party = v.PartyLedgerName, Amount = v.TotalAmount, GSTIN = v.GSTIN ?? "None" }
            ));
        }

        return results;
    }
}

// 18. Missing PAN Where Applicable Rule
public class MissingPanWhereApplicableRule : BaseAuditRule
{
    public override string RuleId => "TDS-PAN-01";
    public override string Name => "Missing PAN on Deductee / Supplier Ledger";
    public override RuleCategory Category => RuleCategory.TDS;
    public override string Description => "Identifies suppliers or contractors with TDS deductions or threshold liabilities where a valid 10-character Income Tax PAN is absent.";

    public MissingPanWhereApplicableRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.High) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT l.Id, l.Name, l.ParentGroup, l.PAN, SUM(ABS(e.Amount)) as TotalDeductionAmount
            FROM Ledgers l
            JOIN VoucherEntries e ON (e.LedgerName = l.Name)
            JOIN Vouchers v ON (e.VoucherId = v.Id AND v.CompanyId = l.CompanyId)
            WHERE l.CompanyId = @CompanyId 
              AND (l.ParentGroup LIKE '%Creditors%' OR l.TaxType = 'TDS' OR l.Name LIKE '%Contractor%' OR l.Name LIKE '%Professional%')
              AND (l.PAN IS NULL OR TRIM(l.PAN) = '' OR LENGTH(TRIM(l.PAN)) != 10)
            GROUP BY l.Id, l.Name;
        ";

        var ledgers = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var l in ledgers)
        {
            decimal amt = l.TotalDeductionAmount;
            var explanation = $"Flagged because vendor '{l.Name}' with active transaction movement ({amt:C2}) has no 10-character PAN recorded, which would require higher rate deduction under Section 206AA.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                ledgerId: l.Id,
                flaggedAmount: amt,
                evidenceObj: new { LedgerName = l.Name, TotalMovement = amt, PAN = l.PAN ?? "None" }
            ));
        }

        return results;
    }
}

// 19. Missing HSN/SAC Code Rule
public class MissingHsnSacRule : BaseAuditRule
{
    public override string RuleId => "GST-HSN-01";
    public override string Name => "Missing HSN or SAC Code on Taxable Head";
    public override RuleCategory Category => RuleCategory.GST;
    public override string Description => "Detects taxable income or expense ledgers that do not have an HSN (Goods) or SAC (Services) code configured.";

    public MissingHsnSacRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Medium) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT Id, Name, ParentGroup, TaxType, GstRate, HsnCode
            FROM Ledgers
            WHERE CompanyId = @CompanyId 
              AND (TaxType = 'GST' OR GstRate > 0 OR ParentGroup IN ('Sales Accounts', 'Purchase Accounts', 'Direct Expenses'))
              AND (HsnCode IS NULL OR TRIM(HsnCode) = '' OR LENGTH(TRIM(HsnCode)) < 4);
        ";

        var ledgers = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var l in ledgers)
        {
            var explanation = $"Flagged because taxable master ledger '{l.Name}' (GST Rate: {l.GstRate ?? 0}%) lacks a standard 4 to 8 digit HSN/SAC tariff classification code.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                ledgerId: l.Id,
                evidenceObj: new { LedgerName = l.Name, l.ParentGroup, l.GstRate, HsnCode = l.HsnCode ?? "None" }
            ));
        }

        return results;
    }
}
