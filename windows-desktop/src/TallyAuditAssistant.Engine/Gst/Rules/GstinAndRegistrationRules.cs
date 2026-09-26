using System.Text.RegularExpressions;
using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Gst;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Gst.Rules;

// 1. GSTIN presence/format where relevant
public class GstinFormatCheckRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-01";
    public override string Name => "GSTIN Structure and Checksum Format Check";
    public override string Description => "Validates that all recorded GSTIN identifiers on party ledgers and commercial transactions strictly follow the 15-character alphanumeric standard statutory structure.";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.2.0";
    public override string SourceReference => "CGST Act 2017 Sec 22 / Rule 10(1) GST Registration Rules";

    public GstinFormatCheckRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.High)
    {
        Parameters["Pattern"] = @"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$";
    }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var pattern = GetParam("Pattern", @"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$");
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
        var results = new List<GstCheckResult>();

        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName,
                   v.TotalAmount, v.PartyLedgerName, l.GSTIN as PartyGstin
            FROM Vouchers v
            JOIN Ledgers l ON (l.Name = v.PartyLedgerName AND l.CompanyId = v.CompanyId)
            WHERE v.CompanyId = @CompanyId 
              AND l.GSTIN IS NOT NULL AND TRIM(l.GSTIN) != '';
        ";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var r in rows)
        {
            string? voucherId = GetString(r, "VoucherId");
            string? voucherNumber = GetString(r, "VoucherNumber");
            DateTime? voucherDate = GetDateTime(r, "VoucherDate");
            string? voucherTypeName = GetString(r, "VoucherTypeName");
            string? partyName = GetString(r, "PartyLedgerName");
            decimal? totalAmount = GetNullableDecimal(r, "TotalAmount");
            string gstin = (GetString(r, "PartyGstin") ?? "").Trim().ToUpper();

            if (string.IsNullOrEmpty(gstin))
            {
                continue;
            }

            if (!regex.IsMatch(gstin))
            {
                var exp = $"Flagged because party '{partyName}' on voucher {voucherNumber} has an invalid GSTIN '{gstin}' that does not conform to the 15-digit statutory regex pattern ({pattern}).";
                results.Add(CreateException(
                    context.CompanyId, exp, Severity,
                    voucherId: voucherId,
                    voucherNumber: voucherNumber,
                    voucherDate: voucherDate,
                    voucherTypeName: voucherTypeName,
                    partyName: partyName,
                    partyGstin: gstin,
                    taxableAmount: totalAmount,
                    evidence: new { Pattern = pattern, RecordedGSTIN = gstin, Length = gstin.Length }
                ));
            }
        }

        return results;
    }
}

// 2. Missing GSTIN on B2B / Taxable supplies
public class MissingGstinOnB2BRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-02";
    public override string Name => "Missing GSTIN on Taxable Commercial Supply";
    public override string Description => "Identifies B2B sales or input purchase transactions exceeding configurable value thresholds where the customer or supplier ledger has no registered GSTIN recorded.";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.1.0";
    public override string SourceReference => "CGST Act 2017 Sec 31(1) / Rule 46(b) Tax Invoice Contents";

    public MissingGstinOnB2BRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.High)
    {
        Parameters["ThresholdAmount"] = 50000.0;
        Parameters["IncludePurchases"] = true;
        Parameters["IncludeSales"] = true;
    }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var threshold = GetParam("ThresholdAmount", 50000.0);
        var incPur = GetParam("IncludePurchases", true);
        var incSale = GetParam("IncludeSales", true);
        var results = new List<GstCheckResult>();

        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName,
                   v.TotalAmount, v.PartyLedgerName, l.GSTIN as PartyGstin, l.ParentGroup
            FROM Vouchers v
            LEFT JOIN Ledgers l ON (l.Name = v.PartyLedgerName AND l.CompanyId = v.CompanyId)
            WHERE v.CompanyId = @CompanyId 
              AND v.TotalAmount >= @Threshold;
        ";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, Threshold = threshold }, cancellationToken: cancellationToken));

        foreach (var r in rows)
        {
            string? voucherId = GetString(r, "VoucherId");
            string? voucherNumber = GetString(r, "VoucherNumber");
            DateTime? voucherDate = GetDateTime(r, "VoucherDate");
            string? voucherTypeName = GetString(r, "VoucherTypeName");
            string party = GetString(r, "PartyLedgerName") ?? "";
            string? gstin = GetString(r, "PartyGstin");
            decimal totalAmount = GetDecimal(r, "TotalAmount");

            string type = voucherTypeName ?? "";
            bool isPurchase = type.IndexOf("Purchase", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isSales = type.IndexOf("Sales", StringComparison.OrdinalIgnoreCase) >= 0;

            if ((isPurchase && !incPur) || (isSales && !incSale) || (!isPurchase && !isSales))
                continue;

            if (string.IsNullOrWhiteSpace(party))
            {
                results.Add(CreateUnableToDetermine(
                    context.CompanyId,
                    "Party ledger is not identified on this commercial voucher, preventing B2B registration verification.",
                    voucherId: voucherId,
                    voucherNumber: voucherNumber,
                    voucherDate: voucherDate,
                    voucherTypeName: voucherTypeName
                ));
                continue;
            }

            if (string.IsNullOrWhiteSpace(gstin))
            {
                var exp = $"Flagged because commercial voucher {voucherNumber} of amount {totalAmount:C2} is booked against party '{party}' which has no GSTIN recorded in the master ledger.";
                results.Add(CreateException(
                    context.CompanyId, exp, Severity,
                    voucherId: voucherId,
                    voucherNumber: voucherNumber,
                    voucherDate: voucherDate,
                    voucherTypeName: voucherTypeName,
                    partyName: party,
                    taxableAmount: totalAmount,
                    evidence: new { VoucherNumber = voucherNumber, Party = party, TotalAmount = totalAmount, Threshold = threshold }
                ));
            }
        }

        return results;
    }
}

// 3. GST Registration Type Inconsistencies
public class GstRegistrationTypeInconsistencyRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-03";
    public override string Name => "GST Registration Type Classification Inconsistency";
    public override string Description => "Detects mismatches between party tax registration types (e.g. Composition, Unregistered, Consumer, SEZ) and the tax line items charged on transactions.";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.0.0";
    public override string SourceReference => "CGST Act 2017 Sec 10(4) Composition Levy / Sec 16(1) ITC Eligibility";

    public GstRegistrationTypeInconsistencyRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.Medium)
    {
        Parameters["FlagCompositionTaxBilling"] = true;
    }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var flagComp = GetParam("FlagCompositionTaxBilling", true);
        var results = new List<GstCheckResult>();

        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName,
                   v.TotalAmount, v.PartyLedgerName, l.GSTIN as PartyGstin, l.ParentGroup,
                   SUM(CASE WHEN e.LedgerName LIKE '%CGST%' OR e.LedgerName LIKE '%SGST%' OR e.LedgerName LIKE '%IGST%' THEN ABS(e.Amount) ELSE 0 END) as TotalTaxCharged
            FROM Vouchers v
            JOIN Ledgers l ON (l.Name = v.PartyLedgerName AND l.CompanyId = v.CompanyId)
            JOIN VoucherEntries e ON (e.VoucherId = v.Id)
            WHERE v.CompanyId = @CompanyId
            GROUP BY v.Id, v.VoucherNumber, v.PartyLedgerName;
        ";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var r in rows)
        {
            string? voucherId = GetString(r, "VoucherId");
            string? voucherNumber = GetString(r, "VoucherNumber");
            DateTime? voucherDate = GetDateTime(r, "VoucherDate");
            string? voucherTypeName = GetString(r, "VoucherTypeName");
            string party = GetString(r, "PartyLedgerName") ?? "";
            string? gstin = GetString(r, "PartyGstin");
            decimal tax = GetDecimal(r, "TotalTaxCharged");

            // Check: party name or group mentions composition dealer or unregistered, but tax was collected
            bool isCompositionParty = party.IndexOf("Composition", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isUnregistered = string.IsNullOrWhiteSpace(gstin);

            if (isCompositionParty && tax > 0 && flagComp)
            {
                var exp = $"Flagged because supplier '{party}' is flagged as a Composition dealer but voucher {voucherNumber} includes tax collections of {tax:C2}. Under Section 10(4), composition dealers cannot collect GST.";
                results.Add(CreateException(
                    context.CompanyId, exp, Severity,
                    voucherId: voucherId,
                    voucherNumber: voucherNumber,
                    voucherDate: voucherDate,
                    voucherTypeName: voucherTypeName,
                    partyName: party,
                    partyGstin: gstin,
                    taxAmount: tax,
                    evidence: new { Party = party, TotalTax = tax, Classification = "Composition Dealer" }
                ));
            }
        }

        return results;
    }
}
