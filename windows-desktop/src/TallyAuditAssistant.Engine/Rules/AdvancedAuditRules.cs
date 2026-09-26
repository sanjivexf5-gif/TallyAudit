using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Rules;

// 1. GST Tax Calculation Consistency Rule
public class GstTaxCalculationConsistencyRule : BaseAuditRule
{
    public override string RuleId => "GST-CON-01";
    public override string Name => "GST Tax Calculation Consistency";
    public override RuleCategory Category => RuleCategory.GST;
    public override string Description => "Compares calculated GST taxes against recorded CGST/SGST/IGST ledger entries to identify mathematical inconsistencies.";

    public GstTaxCalculationConsistencyRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Medium) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id, v.VoucherTypeName, v.VoucherNumber, v.VoucherDate, v.TotalAmount,
                   MAX(CASE WHEN l.TaxType = 'GST' AND l.Name LIKE '%CGST%' THEN ABS(e.Amount) ELSE 0 END) as CgstAmount,
                   MAX(CASE WHEN l.TaxType = 'GST' AND l.Name LIKE '%SGST%' THEN ABS(e.Amount) ELSE 0 END) as SgstAmount,
                   MAX(CASE WHEN l.TaxType = 'GST' AND l.Name LIKE '%IGST%' THEN ABS(e.Amount) ELSE 0 END) as IgstAmount
            FROM Vouchers v
            JOIN VoucherEntries e ON e.VoucherId = v.Id
            JOIN Ledgers l ON (l.CompanyId = v.CompanyId AND l.Name = e.LedgerName)
            WHERE v.CompanyId = @CompanyId
            GROUP BY v.Id
            HAVING CgstAmount > 0 OR SgstAmount > 0 OR IgstAmount > 0;
        ";

        var vouchers = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var v in vouchers)
        {
            decimal cgst = GetDecimal(v, "CgstAmount");
            decimal sgst = GetDecimal(v, "SgstAmount");
            decimal igst = GetDecimal(v, "IgstAmount");
            string? vNum = GetString(v, "VoucherNumber");
            string? vId = GetString(v, "Id");
            DateTime? vDate = GetDateTime(v, "VoucherDate");

            if (cgst > 0 && sgst > 0 && Math.Abs(cgst - sgst) > 1.0m)
            {
                var explanation = $"GST tax calculation inconsistency: CGST amount ({cgst:C2}) does not match SGST amount ({sgst:C2}) for intrastate transaction.";
                results.Add(CreateResult(
                    context.CompanyId,
                    explanation,
                    Severity,
                    voucherId: vId,
                    voucherNumber: vNum,
                    voucherDate: vDate,
                    flaggedAmount: Math.Abs(cgst - sgst),
                    evidenceObj: new { CGST = cgst, SGST = sgst, Discrepancy = Math.Abs(cgst - sgst) }
                ));
            }

            if (cgst > 0 && igst > 0)
            {
                var explanation = "GST tax classification conflict: Both CGST and IGST are charged in the same transaction.";
                results.Add(CreateResult(
                    context.CompanyId,
                    explanation,
                    SeverityLevel.High,
                    voucherId: vId,
                    voucherNumber: vNum,
                    voucherDate: vDate,
                    flaggedAmount: cgst + igst,
                    evidenceObj: new { CGST = cgst, IGST = igst }
                ));
            }
        }

        return results;
    }
}

// 2. Input Tax Credit Review Rule
public class InputTaxCreditReviewRule : BaseAuditRule
{
    public override string RuleId => "GST-ITC-01";
    public override string Name => "Input Tax Credit (ITC) Supplier Review";
    public override RuleCategory Category => RuleCategory.GST;
    public override string Description => "Identifies Input Tax Credit entries where the supplier lacks a valid GSTIN or is marked unregistered.";

    public InputTaxCreditReviewRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.High) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT DISTINCT v.Id, v.VoucherTypeName, v.VoucherNumber, v.VoucherDate, v.TotalAmount, v.PartyLedgerName, pl.GSTIN, e.LedgerName as TaxLedger, e.Amount as TaxAmount
            FROM VoucherEntries e
            JOIN Vouchers v ON e.VoucherId = v.Id
            LEFT JOIN Ledgers pl ON (pl.CompanyId = v.CompanyId AND pl.Name = v.PartyLedgerName)
            WHERE v.CompanyId = @CompanyId
              AND (v.VoucherTypeName LIKE '%Purchase%' OR v.VoucherTypeName LIKE '%Journal%')
              AND (e.LedgerName LIKE '%Input%' OR e.LedgerName LIKE '%CGST Input%' OR e.LedgerName LIKE '%SGST Input%')
              AND e.IsDebit = 1
              AND (pl.GSTIN IS NULL OR TRIM(pl.GSTIN) = '' OR pl.GSTIN = 'Unregistered');
        ";

        var vouchers = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var v in vouchers)
        {
            decimal tax = GetDecimal(v, "TaxAmount");
            string? vNum = GetString(v, "VoucherNumber");
            string? vId = GetString(v, "Id");
            DateTime? vDate = GetDateTime(v, "VoucherDate");
            string party = GetString(v, "PartyLedgerName") ?? string.Empty;
            string? taxLedger = GetString(v, "TaxLedger");
            string supplierGstin = GetString(v, "GSTIN") ?? "None";

            var explanation = $"ITC transaction requires review: Input Tax Credit of {tax:C2} claimed on '{party}' which lacks a valid supplier GSTIN.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: vId,
                voucherNumber: vNum,
                voucherDate: vDate,
                flaggedAmount: tax,
                evidenceObj: new { Supplier = party, TaxLedger = taxLedger, ClaimedITC = tax, SupplierGstin = supplierGstin }
            ));
        }

        return results;
    }
}

// 3. Output GST State Consistency Rule
public class OutputGstReviewRule : BaseAuditRule
{
    public override string RuleId => "GST-OUT-01";
    public override string Name => "Output GST Place of Supply Consistency";
    public override RuleCategory Category => RuleCategory.GST;
    public override string Description => "Checks if the correct tax ledgers (CGST/SGST vs IGST) are used based on the customer state address.";

    public OutputGstReviewRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Medium) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id, v.VoucherTypeName, v.VoucherNumber, v.VoucherDate, v.TotalAmount, v.PartyLedgerName, 
                   c.StateName as CompanyState, pl.StateName as PartyState,
                   SUM(CASE WHEN e.LedgerName LIKE '%CGST%' OR e.LedgerName LIKE '%SGST%' THEN 1 ELSE 0 END) as HasCgSt,
                   SUM(CASE WHEN e.LedgerName LIKE '%IGST%' THEN 1 ELSE 0 END) as HasIgst
            FROM Vouchers v
            JOIN Companies c ON c.Id = v.CompanyId
            LEFT JOIN Ledgers pl ON (pl.CompanyId = v.CompanyId AND pl.Name = v.PartyLedgerName)
            JOIN VoucherEntries e ON e.VoucherId = v.Id
            WHERE v.CompanyId = @CompanyId
              AND v.VoucherTypeName LIKE '%Sales%'
              AND pl.StateName IS NOT NULL AND pl.StateName != ''
            GROUP BY v.Id;
        ";

        var vouchers = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var v in vouchers)
        {
            string compState = GetString(v, "CompanyState") ?? string.Empty;
            string partyState = GetString(v, "PartyState") ?? string.Empty;
            long hasCgSt = GetLong(v, "HasCgSt");
            long hasIgst = GetLong(v, "HasIgst");
            decimal total = GetDecimal(v, "TotalAmount");
            string? vNum = GetString(v, "VoucherNumber");
            string? vId = GetString(v, "Id");
            DateTime? vDate = GetDateTime(v, "VoucherDate");
            string party = GetString(v, "PartyLedgerName") ?? string.Empty;

            bool isInterstate = !compState.Equals(partyState, StringComparison.OrdinalIgnoreCase);

            if (isInterstate && hasCgSt > 0)
            {
                var explanation = $"Potential GST classification inconsistency: Interstate sales transaction to '{party}' ({partyState}) is charged CGST/SGST instead of IGST.";
                results.Add(CreateResult(
                    context.CompanyId,
                    explanation,
                    Severity,
                    voucherId: vId,
                    voucherNumber: vNum,
                    voucherDate: vDate,
                    flaggedAmount: total,
                    evidenceObj: new { Customer = party, CustomerState = partyState, CompanyState = compState, ChargedTax = "CGST/SGST" }
                ));
            }
            else if (!isInterstate && hasIgst > 0)
            {
                var explanation = $"Potential GST classification inconsistency: Intrastate sales transaction to '{party}' ({partyState}) is charged IGST instead of CGST/SGST.";
                results.Add(CreateResult(
                    context.CompanyId,
                    explanation,
                    Severity,
                    voucherId: vId,
                    voucherNumber: vNum,
                    voucherDate: vDate,
                    flaggedAmount: total,
                    evidenceObj: new { Customer = party, CustomerState = partyState, CompanyState = compState, ChargedTax = "IGST" }
                ));
            }
        }

        return results;
    }
}

// 4. TDS Applicability & Threshold Rule
public class TdsApplicabilityThresholdRule : BaseAuditRule
{
    public override string RuleId => "TDS-THR-01";
    public override string Name => "Missing TDS Deduction on Professional / Contracting Expense";
    public override RuleCategory Category => RuleCategory.TDS;
    public override string Description => "Checks if single payments exceed ₹30,000 to contractors or professionals without a matching TDS deduction ledger entry.";

    public TdsApplicabilityThresholdRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.High)
    {
        Parameters["SinglePaymentLimit"] = 30000.0m;
    }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var limit = GetParam("SinglePaymentLimit", 30000.0m);
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string settingSql = "SELECT Value FROM Settings WHERE Key = 'TdsSinglePaymentLimit' LIMIT 1";
        var settingVal = await connection.QuerySingleOrDefaultAsync<string>(settingSql);
        if (decimal.TryParse(settingVal, out var parsedLimit))
        {
            limit = parsedLimit;
        }

        var results = new List<AuditResult>();

        const string sql = @"
            SELECT v.Id, v.VoucherTypeName, v.VoucherNumber, v.VoucherDate, v.TotalAmount, v.PartyLedgerName, e.LedgerName as ExpenseLedger, l.ParentGroup
            FROM Vouchers v
            JOIN VoucherEntries e ON e.VoucherId = v.Id
            JOIN Ledgers l ON (l.CompanyId = v.CompanyId AND l.Name = e.LedgerName)
            WHERE v.CompanyId = @CompanyId
              AND (v.VoucherTypeName LIKE '%Payment%' OR v.VoucherTypeName LIKE '%Journal%' OR v.VoucherTypeName LIKE '%Purchase%')
              AND (l.Name LIKE '%Professional%' OR l.Name LIKE '%Legal%' OR l.Name LIKE '%Audit%' OR l.Name LIKE '%Technical%' OR l.Name LIKE '%Consult%' OR l.Name LIKE '%Contractor%' OR l.Name LIKE '%Rent%')
              AND v.TotalAmount >= @Limit
              AND NOT EXISTS (
                  SELECT 1 FROM VoucherEntries e2
                  JOIN Ledgers l2 ON (l2.CompanyId = v.CompanyId AND l2.Name = e2.LedgerName)
                  WHERE e2.VoucherId = v.Id AND (l2.TaxType = 'TDS' OR l2.Name LIKE '%TDS%' OR l2.Name LIKE '%Tax Deducted%')
              );
        ";

        var vouchers = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, Limit = limit }, cancellationToken: cancellationToken));

        foreach (var v in vouchers)
        {
            decimal amt = GetDecimal(v, "TotalAmount");
            string? vNum = GetString(v, "VoucherNumber");
            string? vId = GetString(v, "Id");
            DateTime? vDate = GetDateTime(v, "VoucherDate");
            string expLedger = GetString(v, "ExpenseLedger") ?? string.Empty;

            var explanation = $"TDS threshold review required: Transaction amount of {amt:C2} under '{expLedger}' exceeds the single-bill limit of {limit:C2} but lacks any recorded TDS withholding.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: vId,
                voucherNumber: vNum,
                voucherDate: vDate,
                flaggedAmount: amt,
                evidenceObj: new { ExpenseType = expLedger, TotalAmount = amt, SingleBillLimit = limit }
            ));
        }

        return results;
    }
}

// 5. Large Transaction Rule
public class LargeTransactionRule : BaseAuditRule
{
    public override string RuleId => "ACC-ANO-03";
    public override string Name => "Material Transaction Value Threshold Outlier";
    public override RuleCategory Category => RuleCategory.AnomalyDetection;
    public override string Description => "Identifies individual high-value sales, purchase, or expense vouchers exceeding configurable material thresholds.";

    public LargeTransactionRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Medium)
    {
        Parameters["MaterialThreshold"] = 1000000.0m;
    }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var threshold = GetParam("MaterialThreshold", 1000000.0m);
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string settingSql = "SELECT Value FROM Settings WHERE Key = 'LargeTransactionThreshold' LIMIT 1";
        var settingVal = await connection.QuerySingleOrDefaultAsync<string>(settingSql);
        if (decimal.TryParse(settingVal, out var parsedThreshold))
        {
            threshold = parsedThreshold;
        }

        var results = new List<AuditResult>();

        const string sql = @"
            SELECT Id, VoucherTypeName, VoucherNumber, VoucherDate, TotalAmount, PartyLedgerName
            FROM Vouchers
            WHERE CompanyId = @CompanyId 
              AND TotalAmount >= @Threshold;
        ";

        var vouchers = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, Threshold = threshold }, cancellationToken: cancellationToken));

        foreach (var v in vouchers)
        {
            decimal amt = GetDecimal(v, "TotalAmount");
            string? vNum = GetString(v, "VoucherNumber");
            string? vId = GetString(v, "Id");
            DateTime? vDate = GetDateTime(v, "VoucherDate");
            string vTypeName = GetString(v, "VoucherTypeName") ?? "Transaction";
            string? party = GetString(v, "PartyLedgerName");

            var explanation = $"Material value threshold outlier: This {vTypeName} transaction has an exceptionally high value of {amt:C2}, exceeding the standard review limit of {threshold:C2}.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: vId,
                voucherNumber: vNum,
                voucherDate: vDate,
                flaggedAmount: amt,
                evidenceObj: new { VoucherTypeName = vTypeName, VoucherNumber = vNum, Amount = amt, Party = party, Threshold = threshold }
            ));
        }

        return results;
    }
}

// 6. Period-End Transaction Review Rule
public class PeriodEndTransactionReviewRule : BaseAuditRule
{
    public override string RuleId => "ACC-TIM-03";
    public override string Name => "Unusual Period-End/Quarter-End Movements";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Checks for high-value transactions or journal adjustments recorded during the last 5 days of any financial quarter.";

    public PeriodEndTransactionReviewRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Medium)
    {
        Parameters["MinPeriodEndAmount"] = 100000.0m;
    }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var minAmount = GetParam("MinPeriodEndAmount", 100000.0m);
        var reviewDays = 5;
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string daySql = "SELECT Value FROM Settings WHERE Key = 'PeriodEndReviewDays' LIMIT 1";
        var settingVal = await connection.QuerySingleOrDefaultAsync<string>(daySql);
        if (int.TryParse(settingVal, out var parsedDays))
        {
            reviewDays = parsedDays;
        }

        var startDay = (31 - reviewDays + 1).ToString("D2");

        var results = new List<AuditResult>();

        var sql = $@"
            SELECT Id, VoucherTypeName, VoucherNumber, VoucherDate, TotalAmount, PartyLedgerName
            FROM Vouchers
            WHERE CompanyId = @CompanyId 
              AND TotalAmount >= @MinAmount
              AND (
                  strftime('%d', VoucherDate) >= '{startDay}' AND strftime('%m', VoucherDate) IN ('03', '06', '09', '12')
              );
        ";

        var vouchers = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, MinAmount = minAmount }, cancellationToken: cancellationToken));

        foreach (var v in vouchers)
        {
            decimal amt = GetDecimal(v, "TotalAmount");
            string? vNum = GetString(v, "VoucherNumber");
            string? vId = GetString(v, "Id");
            DateTime? vDate = GetDateTime(v, "VoucherDate");
            string? party = GetString(v, "PartyLedgerName");

            var explanation = $"Period-end cut-off review required: Transaction value of {amt:C2} posted near financial quarter closing date ({vDate:dd-MMM-yyyy}).";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: vId,
                voucherNumber: vNum,
                voucherDate: vDate,
                flaggedAmount: amt,
                evidenceObj: new { Date = vDate, Amount = amt, Party = party }
            ));
        }

        return results;
    }
}

// 7. Master Data Quality Checks Rule
public class MasterDataQualityCheckRule : BaseAuditRule
{
    public override string RuleId => "MST-QLY-01";
    public override string Name => "Master Data Quality Check";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Audits synchronized party ledgers for duplicated PAN/GSTIN identifiers or suspiciously similar party naming patterns.";

    public MasterDataQualityCheckRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.Low) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT l1.Id as Id1, l1.Name as Name1, l2.Id as Id2, l2.Name as Name2, l1.GSTIN
            FROM Ledgers l1
            JOIN Ledgers l2 ON l1.CompanyId = l2.CompanyId AND l1.Id < l2.Id
            WHERE l1.CompanyId = @CompanyId
              AND (
                  (l1.GSTIN IS NOT NULL AND l1.GSTIN != '' AND l1.GSTIN = l2.GSTIN) OR
                  (l1.PAN IS NOT NULL AND l1.PAN != '' AND l1.PAN = l2.PAN)
              );
        ";

        var duplicates = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var d in duplicates)
        {
            string name1 = GetString(d, "Name1") ?? string.Empty;
            string name2 = GetString(d, "Name2") ?? string.Empty;
            string gstin = GetString(d, "GSTIN") ?? string.Empty;
            string id1 = GetString(d, "Id1") ?? string.Empty;

            var explanation = $"Master data requires review: Duplicate registration credentials found between ledger '{name1}' and '{name2}' (GSTIN: {gstin}).";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                ledgerId: id1,
                evidenceObj: new { Ledger1 = name1, Ledger2 = name2, DuplicateGSTIN = gstin }
            ));
        }

        return results;
    }
}

// 8. Cross-Dataset Consistency Rule
public class CrossDatasetConsistencyCheckRule : BaseAuditRule
{
    public override string RuleId => "ACC-CRS-01";
    public override string Name => "Double Entry Trial Balance Consistency";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Reconciles double entry ledger postings within each individual transaction to guarantee perfect debits and credits balance.";

    public CrossDatasetConsistencyCheckRule(SqliteConnectionFactory connectionFactory) 
        : base(connectionFactory, SeverityLevel.High) { }

    public override async Task<IReadOnlyList<AuditResult>> EvaluateAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id, v.VoucherTypeName, v.VoucherNumber, v.VoucherDate, v.TotalAmount,
                   SUM(CASE WHEN e.IsDebit = 1 THEN e.Amount ELSE 0 END) as TotalDebits,
                   SUM(CASE WHEN e.IsDebit = 0 THEN e.Amount ELSE 0 END) as TotalCredits
            FROM Vouchers v
            JOIN VoucherEntries e ON e.VoucherId = v.Id
            WHERE v.CompanyId = @CompanyId
            GROUP BY v.Id
            HAVING ABS(TotalDebits - TotalCredits) > 1.0;
        ";

        var discrepancies = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var d in discrepancies)
        {
            decimal debits = GetDecimal(d, "TotalDebits");
            decimal credits = GetDecimal(d, "TotalCredits");
            decimal diff = Math.Abs(debits - credits);
            string? vNum = GetString(d, "VoucherNumber");
            string? vId = GetString(d, "Id");
            DateTime? vDate = GetDateTime(d, "VoucherDate");

            var explanation = $"Cross-dataset balance discrepancy: Total debit postings ({debits:C2}) do not balance with credits ({credits:C2}) in voucher {vNum}.";
            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                voucherId: vId,
                voucherNumber: vNum,
                voucherDate: vDate,
                flaggedAmount: diff,
                evidenceObj: new { Debits = debits, Credits = credits, Discrepancy = diff }
            ));
        }

        return results;
    }
}
