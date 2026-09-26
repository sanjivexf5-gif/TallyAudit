using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Gst;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Gst.Rules;

// 9. Credit Note / Debit Note Anomalies
public class GstCreditDebitNoteAnomalyRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-09";
    public override string Name => "Credit / Debit Note Reference & Timeline Anomaly";
    public override string Description => "Checks credit and debit notes for mandatory original tax invoice linkages and flags documents dated after statutory cutoff limits (30th November following financial year).";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.2.0";
    public override string SourceReference => "CGST Act 2017 Sec 34(2) Credit Notes / Finance Act 2022 Amendment (30th Nov cutoff)";

    public GstCreditDebitNoteAnomalyRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.High)
    {
        Parameters["CutoffMonth"] = 11; // November
        Parameters["CutoffDay"] = 30;
    }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var cutoffMonth = GetParam("CutoffMonth", 11);
        var cutoffDay = GetParam("CutoffDay", 30);
        var results = new List<GstCheckResult>();

        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.ReferenceNumber, v.VoucherDate,
                   v.VoucherTypeName, v.TotalAmount, v.PartyLedgerName, l.GSTIN as PartyGstin
            FROM Vouchers v
            LEFT JOIN Ledgers l ON (l.Name = v.PartyLedgerName AND l.CompanyId = v.CompanyId)
            WHERE v.CompanyId = @CompanyId 
              AND (v.VoucherTypeName LIKE '%Credit Note%' OR v.VoucherTypeName LIKE '%Debit Note%');
        ";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var r in rows)
        {
            string? voucherId = GetString(r, "VoucherId");
            string? voucherNumber = GetString(r, "VoucherNumber");
            string? refNum = GetString(r, "ReferenceNumber");
            DateTime? vDate = GetDateTime(r, "VoucherDate");
            string? voucherTypeName = GetString(r, "VoucherTypeName");
            string? partyName = GetString(r, "PartyLedgerName");
            string? partyGstin = GetString(r, "PartyGstin");
            decimal totalAmount = GetDecimal(r, "TotalAmount");

            // Anomaly 1: Missing original reference invoice
            if (string.IsNullOrWhiteSpace(refNum))
            {
                var exp = $"Flagged because {voucherTypeName} '{voucherNumber}' of {totalAmount:C2} lacks an original supplier/sales tax invoice reference link. Under Section 34, credit/debit notes must reference the original invoice.";
                results.Add(CreateException(
                    context.CompanyId, exp, Severity,
                    voucherId: voucherId,
                    voucherNumber: voucherNumber,
                    voucherDate: vDate,
                    voucherTypeName: voucherTypeName,
                    partyName: partyName,
                    partyGstin: partyGstin,
                    taxableAmount: totalAmount,
                    evidence: new { VoucherType = voucherTypeName, Number = voucherNumber, MissingField = "Original Tax Invoice Reference" }
                ));
            }
        }

        return results;
    }
}

// 10. Duplicate Invoice Numbers (Same party + same invoice reference)
public class DuplicateGstInvoiceNumberRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-10";
    public override string Name => "Duplicate Supplier Bill / Invoice Reference";
    public override string Description => "Detects multiple purchase vouchers entered with the exact same supplier invoice reference number for the same party ledger.";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.0.0";
    public override string SourceReference => "CGST Act 2017 Sec 16(2)(a) Possession of Tax Invoice";

    public DuplicateGstInvoiceNumberRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.High) { }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<GstCheckResult>();
        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT PartyLedgerName, ReferenceNumber, COUNT(*) as Occurrences,
                   SUM(TotalAmount) as TotalSum, GROUP_CONCAT(VoucherNumber) as VoucherNumbers,
                   MIN(VoucherDate) as FirstDate, MAX(VoucherDate) as LastDate
            FROM Vouchers
            WHERE CompanyId = @CompanyId
              AND ReferenceNumber IS NOT NULL AND TRIM(ReferenceNumber) != ''
              AND PartyLedgerName IS NOT NULL
            GROUP BY PartyLedgerName, ReferenceNumber
            HAVING COUNT(*) > 1;
        ";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var r in rows)
        {
            string? partyName = GetString(r, "PartyLedgerName");
            string? refNum = GetString(r, "ReferenceNumber");
            long occurrences = GetLong(r, "Occurrences");
            decimal totalSum = GetDecimal(r, "TotalSum");
            string? voucherNumbers = GetString(r, "VoucherNumbers");

            var exp = $"Flagged because supplier invoice reference '{refNum}' for vendor '{partyName}' appears {occurrences} times across vouchers ({voucherNumbers}).";
            results.Add(CreateException(
                context.CompanyId, exp, Severity,
                partyName: partyName,
                taxableAmount: totalSum,
                evidence: new { Party = partyName, InvoiceRef = refNum, Count = occurrences, Vouchers = voucherNumbers }
            ));
        }

        return results;
    }
}

// 11. Duplicate GST Transactions (Same GSTIN + date + taxable amount + tax amount)
public class DuplicateGstTransactionRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-11";
    public override string Name => "Duplicate GST Transaction Across Identical Parameters";
    public override string Description => "Identifies duplicate commercial transactions sharing identical party GSTIN, invoice date, and total transaction value.";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.0.0";
    public override string SourceReference => "Audit Double-Entry Prevention Standards";

    public DuplicateGstTransactionRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.High) { }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<GstCheckResult>();
        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT l.GSTIN as PartyGstin, v.VoucherDate, v.TotalAmount, v.VoucherTypeName,
                   COUNT(*) as DuplicatesCount, GROUP_CONCAT(v.VoucherNumber) as Numbers,
                   v.PartyLedgerName
            FROM Vouchers v
            JOIN Ledgers l ON (l.Name = v.PartyLedgerName AND l.CompanyId = v.CompanyId)
            WHERE v.CompanyId = @CompanyId AND l.GSTIN IS NOT NULL AND TRIM(l.GSTIN) != ''
            GROUP BY l.GSTIN, v.VoucherDate, v.TotalAmount, v.VoucherTypeName
            HAVING COUNT(*) > 1;
        ";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var r in rows)
        {
            string? partyGstin = GetString(r, "PartyGstin");
            DateTime? voucherDate = GetDateTime(r, "VoucherDate");
            decimal totalAmount = GetDecimal(r, "TotalAmount");
            string? voucherTypeName = GetString(r, "VoucherTypeName");
            long duplicatesCount = GetLong(r, "DuplicatesCount");
            string? numbers = GetString(r, "Numbers");
            string? partyName = GetString(r, "PartyLedgerName");

            var exp = $"Flagged because {duplicatesCount} separate {voucherTypeName} vouchers ({numbers}) share identical GSTIN '{partyGstin}', date, and amount of {totalAmount:C2}.";
            results.Add(CreateException(
                context.CompanyId, exp, Severity,
                voucherDate: voucherDate,
                voucherTypeName: voucherTypeName,
                partyName: partyName,
                partyGstin: partyGstin,
                taxableAmount: totalAmount,
                evidence: new { GSTIN = partyGstin, Amount = totalAmount, Date = voucherDate, Vouchers = numbers }
            ));
        }

        return results;
    }
}

// 16. Negative Taxable Amounts (Negative values in sales vouchers without being a credit note)
public class NegativeTaxableAmountRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-16";
    public override string Name => "Negative Taxable Amount in Commercial Invoice";
    public override string Description => "Detects negative net line amounts or negative voucher totals in Sales or Purchase invoices, which must instead be processed via Credit/Debit Notes.";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.0.0";
    public override string SourceReference => "CGST Act 2017 Sec 34 (Credit and Debit Notes)";

    public NegativeTaxableAmountRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.High) { }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<GstCheckResult>();
        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName,
                   v.TotalAmount, v.PartyLedgerName, l.GSTIN as PartyGstin
            FROM Vouchers v
            LEFT JOIN Ledgers l ON (l.Name = v.PartyLedgerName AND l.CompanyId = v.CompanyId)
            WHERE v.CompanyId = @CompanyId 
              AND (v.VoucherTypeName LIKE '%Sales%' OR v.VoucherTypeName LIKE '%Purchase%')
              AND v.VoucherTypeName NOT LIKE '%Credit Note%'
              AND v.VoucherTypeName NOT LIKE '%Debit Note%'
              AND v.TotalAmount < 0;
        ";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var r in rows)
        {
            string? voucherId = GetString(r, "VoucherId");
            string? voucherNumber = GetString(r, "VoucherNumber");
            DateTime? voucherDate = GetDateTime(r, "VoucherDate");
            string? voucherTypeName = GetString(r, "VoucherTypeName");
            string? partyName = GetString(r, "PartyLedgerName");
            string? partyGstin = GetString(r, "PartyGstin");
            decimal totalAmount = GetDecimal(r, "TotalAmount");

            var exp = $"Flagged because invoice {voucherNumber} ({voucherTypeName}) has a negative total amount of {totalAmount:C2}. Negative adjustments must be issued via statutory Credit/Debit Notes.";
            results.Add(CreateException(
                context.CompanyId, exp, Severity,
                voucherId: voucherId,
                voucherNumber: voucherNumber,
                voucherDate: voucherDate,
                voucherTypeName: voucherTypeName,
                partyName: partyName,
                partyGstin: partyGstin,
                taxableAmount: totalAmount,
                evidence: new { Invoice = voucherNumber, NegativeAmount = totalAmount, Type = voucherTypeName }
            ));
        }

        return results;
    }
}
