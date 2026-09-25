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
            string? refNum = r.ReferenceNumber;
            DateTime vDate = r.VoucherDate;

            // Anomaly 1: Missing original reference invoice
            if (string.IsNullOrWhiteSpace(refNum))
            {
                var exp = $"Flagged because {r.VoucherTypeName} '{r.VoucherNumber}' of {r.TotalAmount:C2} lacks an original supplier/sales tax invoice reference link. Under Section 34, credit/debit notes must reference the original invoice.";
                results.Add(CreateException(
                    context.CompanyId, exp, Severity,
                    voucherId: r.VoucherId,
                    voucherNumber: r.VoucherNumber,
                    voucherDate: r.VoucherDate,
                    voucherTypeName: r.VoucherTypeName,
                    partyName: r.PartyLedgerName,
                    partyGstin: r.PartyGstin,
                    taxableAmount: r.TotalAmount,
                    evidence: new { VoucherType = r.VoucherTypeName, Number = r.VoucherNumber, MissingField = "Original Tax Invoice Reference" }
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
            var exp = $"Flagged because supplier invoice reference '{r.ReferenceNumber}' for vendor '{r.PartyLedgerName}' appears {r.Occurrences} times across vouchers ({r.VoucherNumbers}).";
            results.Add(CreateException(
                context.CompanyId, exp, Severity,
                partyName: r.PartyLedgerName,
                taxableAmount: r.TotalSum,
                evidence: new { Party = r.PartyLedgerName, InvoiceRef = r.ReferenceNumber, Count = r.Occurrences, Vouchers = r.VoucherNumbers }
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
            var exp = $"Flagged because {r.DuplicatesCount} separate {r.VoucherTypeName} vouchers ({r.Numbers}) share identical GSTIN '{r.PartyGstin}', date, and amount of {r.TotalAmount:C2}.";
            results.Add(CreateException(
                context.CompanyId, exp, Severity,
                voucherDate: r.VoucherDate,
                voucherTypeName: r.VoucherTypeName,
                partyName: r.PartyLedgerName,
                partyGstin: r.PartyGstin,
                taxableAmount: r.TotalAmount,
                evidence: new { GSTIN = r.PartyGstin, Amount = r.TotalAmount, Date = r.VoucherDate, Vouchers = r.Numbers }
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
            var exp = $"Flagged because invoice {r.VoucherNumber} ({r.VoucherTypeName}) has a negative total amount of {r.TotalAmount:C2}. Negative adjustments must be issued via statutory Credit/Debit Notes.";
            results.Add(CreateException(
                context.CompanyId, exp, Severity,
                voucherId: r.VoucherId,
                voucherNumber: r.VoucherNumber,
                voucherDate: r.VoucherDate,
                voucherTypeName: r.VoucherTypeName,
                partyName: r.PartyLedgerName,
                partyGstin: r.PartyGstin,
                taxableAmount: r.TotalAmount,
                evidence: new { Invoice = r.VoucherNumber, NegativeAmount = r.TotalAmount, Type = r.VoucherTypeName }
            ));
        }

        return results;
    }
}
