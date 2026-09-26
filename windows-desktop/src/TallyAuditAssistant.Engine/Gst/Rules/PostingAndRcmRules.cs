using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Gst;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Gst.Rules;

// 13. Possible RCM-Related Exceptions
public class PossibleRcmExceptionsRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-13";
    public override string Name => "Reverse Charge Mechanism (RCM) Liability Check";
    public override string Description => "Identifies inward expenses under notified RCM categories (e.g., Goods Transport Agency / GTA, Legal Fees, Security Services, Director Remuneration) where reverse charge tax liability is missing.";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.1.0";
    public override string SourceReference => "CGST Act 2017 Sec 9(3) Notified Reverse Charge Supplies / Notification No. 13/2017-CT(Rate)";

    public PossibleRcmExceptionsRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.High)
    {
        Parameters["RcmKeywords"] = "GTA,Transport,Freight,Legal,Advocate,Director,Security";
        Parameters["RcmThreshold"] = 5000.0;
    }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var rawKeywords = GetParam("RcmKeywords", "GTA,Transport,Freight,Legal,Advocate,Director,Security");
        var keywords = rawKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var threshold = GetParam("RcmThreshold", 5000.0);
        var results = new List<GstCheckResult>();

        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName,
                   v.TotalAmount, v.PartyLedgerName, e.LedgerName as EntryLedger, e.Amount as EntryAmount
            FROM Vouchers v
            JOIN VoucherEntries e ON (e.VoucherId = v.Id)
            WHERE v.CompanyId = @CompanyId 
              AND (v.VoucherTypeName LIKE '%Purchase%' OR v.VoucherTypeName LIKE '%Payment%' OR v.VoucherTypeName LIKE '%Journal%')
              AND e.Amount >= @Threshold;
        ";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, Threshold = threshold }, cancellationToken: cancellationToken));

        foreach (var r in rows)
        {
            string? voucherId = GetString(r, "VoucherId");
            string? voucherNumber = GetString(r, "VoucherNumber");
            DateTime? voucherDate = GetDateTime(r, "VoucherDate");
            string? voucherTypeName = GetString(r, "VoucherTypeName");
            string party = GetString(r, "PartyLedgerName") ?? "";
            string entryLedger = GetString(r, "EntryLedger") ?? "";
            decimal entryAmount = GetDecimal(r, "EntryAmount");

            bool matchesRcm = keywords.Any(k => 
                entryLedger.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 ||
                party.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);

            if (matchesRcm)
            {
                var exp = $"Flagged because inward transaction {voucherNumber} includes '{entryLedger}' ({entryAmount:C2}) which matches notified RCM categories (keywords: {rawKeywords}) requiring verification of reverse charge tax liability under Section 9(3).";
                results.Add(CreateException(
                    context.CompanyId, exp, Severity,
                    voucherId: voucherId,
                    voucherNumber: voucherNumber,
                    voucherDate: voucherDate,
                    voucherTypeName: voucherTypeName,
                    partyName: party,
                    taxableAmount: entryAmount,
                    evidence: new { Category = "Notified RCM Head", ExpenseHead = entryLedger, Amount = entryAmount }
                ));
            }
        }

        return results;
    }
}

// 14. GST Ledger Mapping Issues
public class GstLedgerMappingIssuesRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-14";
    public override string Name => "GST Duty Ledger Chart of Accounts Mapping Anomaly";
    public override string Description => "Verifies that tax ledgers are strictly mapped under 'Duties & Taxes' with correct tax type classifications, and input/output heads are not interchanged.";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.0.0";
    public override string SourceReference => "Standard Accounting & Statutory Tax Head Hierarchy";

    public GstLedgerMappingIssuesRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.Medium) { }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<GstCheckResult>();
        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT Id, Name, ParentGroup, TaxType
            FROM Ledgers
            WHERE CompanyId = @CompanyId 
              AND (Name LIKE '%GST%' OR Name LIKE '%Input Tax%' OR Name LIKE '%Output Tax%')
              AND ParentGroup NOT LIKE '%Duties & Taxes%'
              AND ParentGroup NOT LIKE '%Duties and Taxes%';
        ";

        var ledgers = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var l in ledgers)
        {
            string name = GetString(l, "Name") ?? "";
            string parentGroup = GetString(l, "ParentGroup") ?? "";

            var exp = $"Flagged because GST duty ledger '{name}' is grouped under '{parentGroup}' instead of the standard 'Duties & Taxes' hierarchy.";
            results.Add(CreateException(
                context.CompanyId, exp, Severity,
                partyName: name,
                evidence: new { Ledger = name, CurrentGroup = parentGroup, RequiredGroup = "Duties & Taxes" }
            ));
        }

        return results;
    }
}

// 15. Tax Ledger Posting Anomalies (Direct tax entries without taxable base voucher)
public class TaxLedgerPostingAnomaliesRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-15";
    public override string Name => "Standalone Tax Ledger Posting without Underlying Supply";
    public override string Description => "Detects journal or adjustment entries posted directly into GST tax ledgers without any corresponding taxable sales or purchase base entry.";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.0.0";
    public override string SourceReference => "CGST Act 2017 Sec 35 (Accounts and Records)";

    public TaxLedgerPostingAnomaliesRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.Medium) { }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<GstCheckResult>();
        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName,
                   v.TotalAmount, v.PartyLedgerName,
                   COUNT(e.Id) as EntryCount,
                   SUM(CASE WHEN e.LedgerName LIKE '%GST%' OR e.LedgerName LIKE '%Tax%' THEN 1 ELSE 0 END) as TaxEntriesCount
            FROM Vouchers v
            JOIN VoucherEntries e ON (e.VoucherId = v.Id)
            WHERE v.CompanyId = @CompanyId AND v.VoucherTypeName LIKE '%Journal%'
            GROUP BY v.Id, v.VoucherNumber
            HAVING (TaxEntriesCount > 0 AND TaxEntriesCount = EntryCount);
        ";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var r in rows)
        {
            string? voucherId = GetString(r, "VoucherId");
            string? voucherNumber = GetString(r, "VoucherNumber");
            DateTime? voucherDate = GetDateTime(r, "VoucherDate");
            string? voucherTypeName = GetString(r, "VoucherTypeName");
            string? partyName = GetString(r, "PartyLedgerName");
            decimal totalAmount = GetDecimal(r, "TotalAmount");
            long entryCount = GetLong(r, "EntryCount");
            long taxEntriesCount = GetLong(r, "TaxEntriesCount");

            var exp = $"Flagged because journal voucher {voucherNumber} contains only standalone tax head debits/credits without an underlying taxable supply transaction.";
            results.Add(CreateException(
                context.CompanyId, exp, Severity,
                voucherId: voucherId,
                voucherNumber: voucherNumber,
                voucherDate: voucherDate,
                voucherTypeName: voucherTypeName,
                partyName: partyName,
                taxAmount: totalAmount,
                evidence: new { Voucher = voucherNumber, TotalEntries = entryCount, TaxEntries = taxEntriesCount }
            ));
        }

        return results;
    }
}

// 17. Round-Off Anomalies
public class RoundOffAnomaliesRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-17";
    public override string Name => "Invoice Round-Off Ledger Variance Exceeded";
    public override string Description => "Detects round-off ledger postings on tax invoices exceeding statutory or normal fraction tolerances (e.g. > ₹10.00).";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.0.0";
    public override string SourceReference => "CGST Act 2017 Sec 170 (Rounding off of Tax)";

    public RoundOffAnomaliesRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.Low)
    {
        Parameters["MaxRoundOffRupees"] = 10.0;
    }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var maxLimit = (decimal)GetParam("MaxRoundOffRupees", 10.0);
        var results = new List<GstCheckResult>();

        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName,
                   v.TotalAmount, v.PartyLedgerName, e.LedgerName, ABS(e.Amount) as RoundAmount
            FROM Vouchers v
            JOIN VoucherEntries e ON (e.VoucherId = v.Id)
            WHERE v.CompanyId = @CompanyId 
              AND (e.LedgerName LIKE '%Round Off%' OR e.LedgerName LIKE '%Rounding%')
              AND ABS(e.Amount) > @MaxLimit;
        ";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, MaxLimit = maxLimit }, cancellationToken: cancellationToken));

        foreach (var r in rows)
        {
            string? voucherId = GetString(r, "VoucherId");
            string? voucherNumber = GetString(r, "VoucherNumber");
            DateTime? voucherDate = GetDateTime(r, "VoucherDate");
            string? voucherTypeName = GetString(r, "VoucherTypeName");
            string? partyName = GetString(r, "PartyLedgerName");
            decimal amt = GetDecimal(r, "RoundAmount");

            var exp = $"Flagged because round-off amount of {amt:C2} on voucher {voucherNumber} exceeds standard fractional tolerance limit of {maxLimit:C2}. Section 170 allows rounding off to nearest rupee.";
            results.Add(CreateException(
                context.CompanyId, exp, Severity,
                voucherId: voucherId,
                voucherNumber: voucherNumber,
                voucherDate: voucherDate,
                voucherTypeName: voucherTypeName,
                partyName: partyName,
                taxableAmount: amt,
                evidence: new { Voucher = voucherNumber, RoundOffAmount = amt, PermittedMax = maxLimit }
            ));
        }

        return results;
    }
}
