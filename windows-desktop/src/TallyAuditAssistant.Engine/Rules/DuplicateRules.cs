using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Rules;

// 1. Duplicate Voucher Rule
public class DuplicateVoucherRule : BaseAuditRule
{
    public override string RuleId => "ACC-DUP-01";
    public override string Name => "Duplicate Voucher Identifier";
    public override RuleCategory Category => RuleCategory.DuplicateDetection;
    public override string Description => "Detects multiple vouchers recorded with the identical voucher number under the same voucher type.";

    public DuplicateVoucherRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.High) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT VoucherTypeName, VoucherNumber, COUNT(*) as Occurrences, MIN(VoucherDate) as FirstDate, MAX(VoucherDate) as LastDate, TotalAmount, GROUP_CONCAT(Id) as VoucherIds
            FROM Vouchers
            WHERE CompanyId = @CompanyId AND VoucherNumber IS NOT NULL AND TRIM(VoucherNumber) != ''
            GROUP BY VoucherTypeName, VoucherNumber
            HAVING COUNT(*) > 1;
        ";

        var duplicates = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var dup in duplicates)
        {
            string vTypeName = dup.VoucherTypeName;
            string vNum = dup.VoucherNumber;
            long count = dup.Occurrences;
            decimal amount = dup.TotalAmount;
            string ids = dup.VoucherIds;

            var explanation = $"Flagged because voucher number '{vNum}' under voucher type '{vTypeName}' appears {count} times in the records with amount {amount:C2}.";

            var result = CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: ids.Split(',').FirstOrDefault(),
                voucherNumber: vNum,
                flaggedAmount: amount,
                evidenceObj: new { VoucherType = vTypeName, VoucherNumber = vNum, DuplicateCount = count, TotalAmount = amount, MatchingIds = ids }
            );

            results.Add(result);
        }

        return results;
    }
}

// 2. Duplicate Invoice Number Rule
public class DuplicateInvoiceNumberRule : BaseAuditRule
{
    public override string RuleId => "ACC-DUP-02";
    public override string Name => "Duplicate Supplier Invoice Reference";
    public override RuleCategory Category => RuleCategory.DuplicateDetection;
    public override string Description => "Detects identical supplier invoice reference numbers recorded across multiple vouchers for the same party.";

    public DuplicateInvoiceNumberRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.High) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT PartyLedgerName, ReferenceNumber, COUNT(*) as Occurrences, TotalAmount, GROUP_CONCAT(Id) as VoucherIds, GROUP_CONCAT(VoucherNumber) as VoucherNumbers
            FROM Vouchers
            WHERE CompanyId = @CompanyId AND ReferenceNumber IS NOT NULL AND TRIM(ReferenceNumber) != '' AND PartyLedgerName IS NOT NULL
            GROUP BY PartyLedgerName, ReferenceNumber
            HAVING COUNT(*) > 1;
        ";

        var duplicates = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var dup in duplicates)
        {
            string party = dup.PartyLedgerName;
            string refNum = dup.ReferenceNumber;
            long count = dup.Occurrences;
            decimal amount = dup.TotalAmount;
            string vNums = dup.VoucherNumbers;

            var explanation = $"Flagged because supplier invoice reference '{refNum}' for vendor '{party}' is entered {count} times across vouchers ({vNums}).";

            var result = CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherNumber: refNum,
                flaggedAmount: amount,
                evidenceObj: new { Party = party, ReferenceNumber = refNum, Count = count, Vouchers = vNums }
            );

            results.Add(result);
        }

        return results;
    }
}
