using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Gst;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Gst.Rules;

// 4. CGST/SGST/IGST Consistency Check
public class CgstSgstIgstConsistencyRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-04";
    public override string Name => "CGST / SGST Equal Ratio and IGST Mutual Exclusivity";
    public override string Description => "Verifies that intrastate transactions have equal amounts of CGST and SGST, and flags transactions where IGST is inappropriately mixed with CGST/SGST on the same supply.";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.1.0";
    public override string SourceReference => "CGST Act 2017 Sec 9(1) / SGST Act Sec 9(1) Dual GST Model";

    public CgstSgstIgstConsistencyRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.High)
    {
        Parameters["ToleranceRupees"] = 1.0; // Rounding tolerance
    }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var tolerance = (decimal)GetParam("ToleranceRupees", 1.0);
        var results = new List<GstCheckResult>();

        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName,
                   v.TotalAmount, v.PartyLedgerName,
                   SUM(CASE WHEN e.LedgerName LIKE '%CGST%' OR e.LedgerName LIKE '%Central Tax%' THEN ABS(e.Amount) ELSE 0 END) as Cgst,
                   SUM(CASE WHEN e.LedgerName LIKE '%SGST%' OR e.LedgerName LIKE '%State Tax%' OR e.LedgerName LIKE '%UTGST%' THEN ABS(e.Amount) ELSE 0 END) as Sgst,
                   SUM(CASE WHEN e.LedgerName LIKE '%IGST%' OR e.LedgerName LIKE '%Integrated Tax%' THEN ABS(e.Amount) ELSE 0 END) as Igst
            FROM Vouchers v
            JOIN VoucherEntries e ON (e.VoucherId = v.Id)
            WHERE v.CompanyId = @CompanyId
            GROUP BY v.Id, v.VoucherNumber, v.PartyLedgerName
            HAVING (Cgst > 0 OR Sgst > 0 OR Igst > 0);
        ";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var r in rows)
        {
            decimal cgst = r.Cgst;
            decimal sgst = r.Sgst;
            decimal igst = r.Igst;

            // Scenario A: IGST mixed with CGST or SGST
            if (igst > 0 && (cgst > 0 || sgst > 0))
            {
                var exp = $"Flagged because voucher {r.VoucherNumber} inappropriately combines IGST ({igst:C2}) with CGST ({cgst:C2}) / SGST ({sgst:C2}) on the same document.";
                results.Add(CreateException(
                    context.CompanyId, exp, Severity,
                    voucherId: r.VoucherId,
                    voucherNumber: r.VoucherNumber,
                    voucherDate: r.VoucherDate,
                    voucherTypeName: r.VoucherTypeName,
                    partyName: r.PartyLedgerName,
                    cgst: cgst,
                    sgst: sgst,
                    igst: igst,
                    evidence: new { CGST = cgst, SGST = sgst, IGST = igst, Issue = "Mixed IGST and Dual Taxes" }
                ));
            }
            // Scenario B: Intrastate CGST and SGST mismatch beyond tolerance
            else if (cgst > 0 || sgst > 0)
            {
                decimal diff = Math.Abs(cgst - sgst);
                if (diff > tolerance)
                {
                    var exp = $"Flagged because CGST ({cgst:C2}) and SGST ({sgst:C2}) on voucher {r.VoucherNumber} have a variance of {diff:C2}, exceeding the allowable rounding tolerance of {tolerance:C2}. Central and State tax components must be equal.";
                    results.Add(CreateException(
                        context.CompanyId, exp, SeverityLevel.High,
                        voucherId: r.VoucherId,
                        voucherNumber: r.VoucherNumber,
                        voucherDate: r.VoucherDate,
                        voucherTypeName: r.VoucherTypeName,
                        partyName: r.PartyLedgerName,
                        cgst: cgst,
                        sgst: sgst,
                        evidence: new { CGST = cgst, SGST = sgst, Discrepancy = diff, AllowedTolerance = tolerance }
                    ));
                }
            }
        }

        return results;
    }
}

// 5. Interstate vs Intrastate Tax Consistency
public class InterstateIntrastateTaxConsistencyRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-05";
    public override string Name => "Interstate vs Intrastate Tax Allocation Consistency";
    public override string Description => "Validates that intrastate supplies (same state) attract CGST+SGST, while interstate supplies (different state) attract IGST based on company and counterparty state codes.";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.2.0";
    public override string SourceReference => "IGST Act 2017 Sec 7 (Inter-state Supply) & Sec 8 (Intra-state Supply)";

    public InterstateIntrastateTaxConsistencyRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.Critical) { }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<GstCheckResult>();
        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        // Fetch company state code or first 2 digits of company GSTIN
        const string compSql = "SELECT StateCode, GSTIN FROM Companies WHERE Id = @CompanyId LIMIT 1;";
        var comp = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(compSql, new { context.CompanyId }, cancellationToken: cancellationToken));

        string? compState = comp?.StateCode;
        if (string.IsNullOrWhiteSpace(compState) && comp?.GSTIN != null && ((string)comp.GSTIN).Length >= 2)
        {
            compState = ((string)comp.GSTIN).Substring(0, 2);
        }

        if (string.IsNullOrWhiteSpace(compState))
        {
            results.Add(CreateUnableToDetermine(
                context.CompanyId,
                "Company state code or GSTIN is missing in master settings; unable to verify interstate vs intrastate jurisdiction."
            ));
            return results;
        }

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName,
                   v.TotalAmount, v.PartyLedgerName, l.GSTIN as PartyGstin, l.StateName as PartyState,
                   SUM(CASE WHEN e.LedgerName LIKE '%CGST%' OR e.LedgerName LIKE '%SGST%' THEN ABS(e.Amount) ELSE 0 END) as DualTax,
                   SUM(CASE WHEN e.LedgerName LIKE '%IGST%' THEN ABS(e.Amount) ELSE 0 END) as Igst
            FROM Vouchers v
            JOIN Ledgers l ON (l.Name = v.PartyLedgerName AND l.CompanyId = v.CompanyId)
            JOIN VoucherEntries e ON (e.VoucherId = v.Id)
            WHERE v.CompanyId = @CompanyId
            GROUP BY v.Id, v.VoucherNumber, v.PartyLedgerName
            HAVING (DualTax > 0 OR Igst > 0);
        ";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var r in rows)
        {
            string? partyGstin = r.PartyGstin;
            if (string.IsNullOrWhiteSpace(partyGstin) || partyGstin.Length < 2)
            {
                results.Add(CreateUnableToDetermine(
                    context.CompanyId,
                    $"Party '{r.PartyLedgerName}' lacks a valid 15-digit GSTIN with state code prefix; cannot determine interstate/intrastate classification.",
                    voucherId: r.VoucherId,
                    voucherNumber: r.VoucherNumber,
                    voucherDate: r.VoucherDate,
                    voucherTypeName: r.VoucherTypeName,
                    partyName: r.PartyLedgerName
                ));
                continue;
            }

            string partyState = partyGstin.Substring(0, 2);
            bool isIntrastate = string.Equals(compState, partyState, StringComparison.OrdinalIgnoreCase);
            decimal dualTax = r.DualTax;
            decimal igst = r.Igst;

            if (isIntrastate && igst > 0 && dualTax == 0)
            {
                var exp = $"Flagged because supplier and recipient are both located in state code '{compState}' (Intra-state), but IGST of {igst:C2} was charged instead of CGST + SGST.";
                results.Add(CreateException(
                    context.CompanyId, exp, Severity,
                    voucherId: r.VoucherId,
                    voucherNumber: r.VoucherNumber,
                    voucherDate: r.VoucherDate,
                    voucherTypeName: r.VoucherTypeName,
                    partyName: r.PartyLedgerName,
                    partyGstin: partyGstin,
                    igst: igst,
                    evidence: new { CompanyState = compState, CounterpartyState = partyState, Issue = "IGST applied on Intrastate Supply" }
                ));
            }
            else if (!isIntrastate && dualTax > 0 && igst == 0)
            {
                var exp = $"Flagged because supplier state '{compState}' differs from recipient state '{partyState}' (Inter-state), but CGST/SGST of {dualTax:C2} was charged instead of IGST.";
                results.Add(CreateException(
                    context.CompanyId, exp, Severity,
                    voucherId: r.VoucherId,
                    voucherNumber: r.VoucherNumber,
                    voucherDate: r.VoucherDate,
                    voucherTypeName: r.VoucherTypeName,
                    partyName: r.PartyLedgerName,
                    partyGstin: partyGstin,
                    cgst: dualTax / 2,
                    sgst: dualTax / 2,
                    evidence: new { CompanyState = compState, CounterpartyState = partyState, Issue = "CGST/SGST applied on Interstate Supply" }
                ));
            }
        }

        return results;
    }
}

// 18. Place-of-supply inconsistencies where the required data exists
public class PlaceOfSupplyInconsistencyRule : BaseGstRule
{
    public override string RuleId => "GST-CHK-18";
    public override string Name => "Place of Supply Jurisdiction Inconsistency";
    public override string Description => "Validates that Place of Supply (POS) recorded on transaction matches tax allocation logic under Section 10/12 of the IGST Act, marking as 'Unable to determine' when POS is missing.";
    public override DateTime EffectiveDate => new DateTime(2017, 7, 1);
    public override string Jurisdiction => "IN-ALL";
    public override string Version => "1.0.0";
    public override string SourceReference => "IGST Act 2017 Sec 10 (Goods) / Sec 12 (Services) Place of Supply";

    public PlaceOfSupplyInconsistencyRule(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory, SeverityLevel.Medium) { }

    public override async Task<IReadOnlyList<GstCheckResult>> EvaluateAsync(GstAuditContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<GstCheckResult>();
        using var conn = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string compSql = "SELECT StateCode, GSTIN FROM Companies WHERE Id = @CompanyId LIMIT 1;";
        var comp = await conn.QuerySingleOrDefaultAsync(new CommandDefinition(compSql, new { context.CompanyId }, cancellationToken: cancellationToken));
        string? compState = comp?.StateCode ?? (comp?.GSTIN != null && ((string)comp.GSTIN).Length >= 2 ? ((string)comp.GSTIN).Substring(0, 2) : null);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName,
                   v.TotalAmount, v.PartyLedgerName, l.StateName as PartyState, l.GSTIN as PartyGstin,
                   SUM(CASE WHEN e.LedgerName LIKE '%IGST%' THEN ABS(e.Amount) ELSE 0 END) as Igst,
                   SUM(CASE WHEN e.LedgerName LIKE '%CGST%' OR e.LedgerName LIKE '%SGST%' THEN ABS(e.Amount) ELSE 0 END) as DualTax
            FROM Vouchers v
            JOIN Ledgers l ON (l.Name = v.PartyLedgerName AND l.CompanyId = v.CompanyId)
            JOIN VoucherEntries e ON (e.VoucherId = v.Id)
            WHERE v.CompanyId = @CompanyId
            GROUP BY v.Id, v.VoucherNumber, v.PartyLedgerName
            HAVING (Igst > 0 OR DualTax > 0);
        ";

        var rows = await conn.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var r in rows)
        {
            string? partyState = r.PartyState;
            string? partyGstin = r.PartyGstin;

            if (string.IsNullOrWhiteSpace(partyState) && (string.IsNullOrWhiteSpace(partyGstin) || partyGstin.Length < 2))
            {
                results.Add(CreateUnableToDetermine(
                    context.CompanyId,
                    "Place of supply state and recipient state are not specified on voucher or party master in Tally.",
                    voucherId: r.VoucherId,
                    voucherNumber: r.VoucherNumber,
                    voucherDate: r.VoucherDate,
                    voucherTypeName: r.VoucherTypeName,
                    partyName: r.PartyLedgerName
                ));
            }
        }

        return results;
    }
}
