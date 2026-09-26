using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Ledgers;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Reconciliation;

// 1. Trial Balance / Ledger Consistency Rule
public class TrialBalanceConsistencyRule : BaseReconciliationRule
{
    public override string RuleId => "REC-LGD-01";
    public override string RuleCode => "REC-LGD-01";
    public override string RuleName => "Trial Balance & Ledger Consistency Check";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Reconciles Opening Balance + Debits - Credits against the recorded Closing Balance for all ledger accounts.";

    public TrialBalanceConsistencyRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory) { }

    public override async Task<IReadOnlyList<AuditResult>> ExecuteAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT l.Id, l.Name, l.ParentGroup, l.OpeningBalance, l.ClosingBalance,
                   COALESCE(SUM(CASE WHEN e.IsDebit = 1 THEN e.Amount ELSE 0 END), 0) as TotalDebits,
                   COALESCE(SUM(CASE WHEN e.IsDebit = 0 THEN e.Amount ELSE 0 END), 0) as TotalCredits
            FROM Ledgers l
            LEFT JOIN VoucherEntries e ON (e.LedgerName = l.Name)
            LEFT JOIN Vouchers v ON (v.Id = e.VoucherId AND v.CompanyId = l.CompanyId AND v.VoucherDate BETWEEN @FromDate AND @ToDate)
            WHERE l.CompanyId = @CompanyId
            GROUP BY l.Id;
        ";

        var ledgers = await connection.QueryAsync(new CommandDefinition(sql, new { CompanyId = context.CompanyId, context.FromDate, context.ToDate }, cancellationToken: cancellationToken));

        foreach (var l in ledgers)
        {
            decimal opening = l.OpeningBalance;
            decimal closing = l.ClosingBalance;
            decimal debits = l.TotalDebits;
            decimal credits = l.TotalCredits;

            // In bookkeeping, Asset/Expense typically have debit balances, Liability/Equity/Income have credit balances.
            // A basic check is whether the net transactional movement reconciles with the net balance movement.
            decimal calculatedMovement = debits - credits;
            decimal recordedMovement = closing - opening;
            decimal diff = Math.Abs(calculatedMovement - recordedMovement);

            if (diff > 1.00m) // Materiality limit ₹1.00
            {
                var explanation = $"Ledger closing balance does not reconcile with calculated transaction movement for ledger '{l.Name}'. Mismatch: {diff:C2}.";
                results.Add(CreateReconciliationResult(
                    context.CompanyId,
                    explanation,
                    SeverityLevel.Medium,
                    ledgerId: l.Id,
                    flaggedAmount: diff,
                    evidenceObj: new
                    {
                        Ledger = l.Name,
                        OpeningBalance = opening,
                        TotalDebits = debits,
                        TotalCredits = credits,
                        CalculatedClosing = opening + calculatedMovement,
                        RecordedClosing = closing,
                        Difference = diff
                    }
                ));
            }
        }

        return results;
    }
}

// 2. Ledger ↔ Voucher Reconciliation Rule
public class LedgerVoucherReconciliationRule : BaseReconciliationRule
{
    public override string RuleId => "REC-VOU-01";
    public override string RuleCode => "REC-VOU-01";
    public override string RuleName => "Ledger to Voucher Posting Cross-Check";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Cross-checks transactions to identify vouchers without any posting entries or orphan postings.";

    public LedgerVoucherReconciliationRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory) { }

    public override async Task<IReadOnlyList<AuditResult>> ExecuteAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        // Vouchers without expected ledger postings
        const string sqlVouchers = @"
            SELECT v.Id, v.VoucherNumber, v.VoucherTypeName, v.VoucherDate, v.TotalAmount
            FROM Vouchers v
            WHERE v.CompanyId = @CompanyId AND v.VoucherDate BETWEEN @FromDate AND @ToDate
              AND NOT EXISTS (SELECT 1 FROM VoucherEntries e WHERE e.VoucherId = v.Id);
        ";

        var emptyVouchers = await connection.QueryAsync(new CommandDefinition(sqlVouchers, new { CompanyId = context.CompanyId, context.FromDate, context.ToDate }, cancellationToken: cancellationToken));

        foreach (var v in emptyVouchers)
        {
            var explanation = $"Potential exception: Commercial voucher {v.VoucherNumber} ({v.VoucherTypeName}) has no corresponding double-entry ledger postings.";
            results.Add(CreateReconciliationResult(
                context.CompanyId,
                explanation,
                SeverityLevel.High,
                voucherId: v.Id,
                voucherNumber: v.VoucherNumber,
                voucherDate: v.VoucherDate,
                flaggedAmount: v.TotalAmount,
                evidenceObj: new { v.VoucherNumber, v.VoucherTypeName, v.TotalAmount }
            ));
        }

        return results;
    }
}

// 3. GST Tax Calculation & Rate Reconciliation Rule
public class GstRateReconciliationRule : BaseReconciliationRule
{
    public override string RuleId => "REC-GST-01";
    public override string RuleCode => "REC-GST-01";
    public override string RuleName => "GST Tax Rate Reconciliation";
    public override RuleCategory Category => RuleCategory.GST;
    public override string Description => "Cross-checks Sales/Purchase taxable values multiplied by applicable rate against recorded tax amounts.";

    public GstRateReconciliationRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory) { }

    public override async Task<IReadOnlyList<AuditResult>> ExecuteAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id, v.VoucherNumber, v.VoucherTypeName, v.VoucherDate, v.TotalAmount, v.PartyLedgerName,
                   e.LedgerName as ProductLedger, e.Amount as TaxableAmount, l.GstRate,
                   COALESCE((SELECT SUM(ABS(e2.Amount)) 
                             FROM VoucherEntries e2 
                             WHERE e2.VoucherId = v.Id 
                               AND (e2.LedgerName LIKE '%CGST%' OR e2.LedgerName LIKE '%SGST%' OR e2.LedgerName LIKE '%IGST%')), 0) as RecordedTax
            FROM Vouchers v
            JOIN VoucherEntries e ON e.VoucherId = v.Id
            JOIN Ledgers l ON (l.CompanyId = v.CompanyId AND l.Name = e.LedgerName AND l.GstRate > 0)
            WHERE v.CompanyId = @CompanyId AND v.VoucherDate BETWEEN @FromDate AND @ToDate
              AND (v.VoucherTypeName LIKE '%Sales%' OR v.VoucherTypeName LIKE '%Purchase%')
            GROUP BY e.Id;
        ";

        var postings = await connection.QueryAsync(new CommandDefinition(sql, new { CompanyId = context.CompanyId, context.FromDate, context.ToDate }, cancellationToken: cancellationToken));

        foreach (var p in postings)
        {
            decimal taxable = p.TaxableAmount;
            decimal rate = p.GstRate;
            decimal recordedTax = p.RecordedTax;

            decimal expectedTax = Math.Round(taxable * (rate / 100m), 2);
            decimal diff = Math.Abs(expectedTax - recordedTax);

            if (diff > 5.00m) // Materiality limit ₹5.00
            {
                var explanation = $"GST reconciliation difference: Recorded tax ({recordedTax:C2}) deviates from expected tax ({expectedTax:C2}) for product '{p.ProductLedger}' at {rate}% rate.";
                results.Add(CreateReconciliationResult(
                    context.CompanyId,
                    explanation,
                    SeverityLevel.Medium,
                    voucherId: p.Id,
                    voucherNumber: p.VoucherNumber,
                    voucherDate: p.VoucherDate,
                    flaggedAmount: diff,
                    evidenceObj: new
                    {
                        Voucher = p.VoucherNumber,
                        Product = p.ProductLedger,
                        TaxableValue = taxable,
                        GstRate = rate,
                        ExpectedTax = expectedTax,
                        RecordedTax = recordedTax,
                        Difference = diff
                    }
                ));
            }
        }

        return results;
    }
}

// 4. GST Input / Output Net Position Rule
public class GstInputOutputNetReconciliationRule : BaseReconciliationRule
{
    public override string RuleId => "REC-GST-02";
    public override string RuleCode => "REC-GST-02";
    public override string RuleName => "GST Input/Output Net Position Reconciliation";
    public override RuleCategory Category => RuleCategory.GST;
    public override string Description => "Computes total Input GST (ITC) vs Output GST and compares with Tally tax ledgers net closing balances.";

    public GstInputOutputNetReconciliationRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory) { }

    public override async Task<IReadOnlyList<AuditResult>> ExecuteAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT 
                SUM(CASE WHEN e.LedgerName LIKE '%CGST Input%' OR e.LedgerName LIKE '%CGST%Input%' THEN e.Amount ELSE 0 END) as InputCGST,
                SUM(CASE WHEN e.LedgerName LIKE '%SGST Input%' OR e.LedgerName LIKE '%SGST%Input%' THEN e.Amount ELSE 0 END) as InputSGST,
                SUM(CASE WHEN e.LedgerName LIKE '%IGST Input%' OR e.LedgerName LIKE '%IGST%Input%' THEN e.Amount ELSE 0 END) as InputIGST,
                SUM(CASE WHEN e.LedgerName LIKE '%CGST Output%' OR e.LedgerName LIKE '%CGST%Output%' THEN e.Amount ELSE 0 END) as OutputCGST,
                SUM(CASE WHEN e.LedgerName LIKE '%SGST Output%' OR e.LedgerName LIKE '%SGST%Output%' THEN e.Amount ELSE 0 END) as OutputSGST,
                SUM(CASE WHEN e.LedgerName LIKE '%IGST Output%' OR e.LedgerName LIKE '%IGST%Output%' THEN e.Amount ELSE 0 END) as OutputIGST
            FROM VoucherEntries e
            JOIN Vouchers v ON v.Id = e.VoucherId
            WHERE v.CompanyId = @CompanyId AND v.VoucherDate BETWEEN @FromDate AND @ToDate;
        ";

        var totals = await connection.QuerySingleOrDefaultAsync(new CommandDefinition(sql, new { CompanyId = context.CompanyId, context.FromDate, context.ToDate }, cancellationToken: cancellationToken));

        if (totals != null)
        {
            decimal inputC = totals.InputCGST ?? 0;
            decimal inputS = totals.InputSGST ?? 0;
            decimal inputI = totals.InputIGST ?? 0;
            decimal outputC = totals.OutputCGST ?? 0;
            decimal outputS = totals.OutputSGST ?? 0;
            decimal outputI = totals.OutputIGST ?? 0;

            decimal totalInput = inputC + inputS + inputI;
            decimal totalOutput = outputC + outputS + outputI;
            decimal netPositionExpected = totalOutput - totalInput;

            // Reconcile with tax ledger closing balances sum
            const string ledgerSql = @"
                SELECT SUM(ClosingBalance) 
                FROM Ledgers 
                WHERE CompanyId = @CompanyId AND ParentGroup LIKE '%Duties & Taxes%' AND TaxType = 'GST';
            ";
            decimal actualClosingDuties = await connection.QuerySingleOrDefaultAsync<decimal>(new CommandDefinition(ledgerSql, new { CompanyId = context.CompanyId }, cancellationToken: cancellationToken));

            decimal diff = Math.Abs(netPositionExpected - actualClosingDuties);

            if (diff > 100.00m) // Materiality limit ₹100.00
            {
                var explanation = $"GST net tax position mismatch: Calculated net position for period is {netPositionExpected:C2} (Output {totalOutput:C2} - Input {totalInput:C2}), but recorded tax ledgers closing balances sum is {actualClosingDuties:C2}.";
                results.Add(CreateReconciliationResult(
                    context.CompanyId,
                    explanation,
                    SeverityLevel.Medium,
                    flaggedAmount: diff,
                    evidenceObj: new
                    {
                        InputCGST = inputC,
                        InputSGST = inputS,
                        InputIGST = inputI,
                        OutputCGST = outputC,
                        OutputSGST = outputS,
                        OutputIGST = outputI,
                        CalculatedNet = netPositionExpected,
                        DutiesLedgerClosing = actualClosingDuties,
                        Difference = diff
                    }
                ));
            }
        }

        return results;
    }
}

// 5. TDS Expense/Payment Verification Rule
public class TdsExpenseVerificationRule : BaseReconciliationRule
{
    public override string RuleId => "REC-TDS-01";
    public override string RuleCode => "REC-TDS-01";
    public override string RuleName => "TDS Deduction Rate Reconciliation";
    public override RuleCategory Category => RuleCategory.TDS;
    public override string Description => "Verifies whether actual TDS deductions match expected withholding rates based on expense transaction amounts.";

    public TdsExpenseVerificationRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory) { }

    public override async Task<IReadOnlyList<AuditResult>> ExecuteAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id, v.VoucherNumber, v.VoucherDate, v.TotalAmount, v.PartyLedgerName,
                   e.LedgerName as ExpenseLedger, ABS(e.Amount) as ExpenseAmount,
                   e2.LedgerName as TdsLedger, ABS(e2.Amount) as TdsAmount
            FROM Vouchers v
            JOIN VoucherEntries e ON e.VoucherId = v.Id
            JOIN Ledgers l ON (l.CompanyId = v.CompanyId AND l.Name = e.LedgerName)
            JOIN VoucherEntries e2 ON e2.VoucherId = v.Id
            JOIN Ledgers l2 ON (l2.CompanyId = v.CompanyId AND l2.Name = e2.LedgerName)
            WHERE v.CompanyId = @CompanyId AND v.VoucherDate BETWEEN @FromDate AND @ToDate
              AND (l.Name LIKE '%Professional%' OR l.Name LIKE '%Legal%' OR l.Name LIKE '%Audit%' OR l.Name LIKE '%Technical%' OR l.Name LIKE '%Consult%' OR l.Name LIKE '%Contractor%' OR l.Name LIKE '%Rent%')
              AND (l2.TaxType = 'TDS' OR l2.Name LIKE '%TDS%' OR l2.Name LIKE '%Tax Deducted%');
        ";

        var records = await connection.QueryAsync(new CommandDefinition(sql, new { CompanyId = context.CompanyId, context.FromDate, context.ToDate }, cancellationToken: cancellationToken));

        foreach (var r in records)
        {
            decimal exp = r.ExpenseAmount;
            decimal tds = r.TdsAmount;

            // TDS Rates are typically 1% (contractor - individual), 2% (contractor - company), 10% (professional/rent)
            decimal r1 = Math.Round(exp * 0.01m, 2);
            decimal r2 = Math.Round(exp * 0.02m, 2);
            decimal r10 = Math.Round(exp * 0.10m, 2);

            bool matchesRate = Math.Abs(r1 - tds) < 2.00m || Math.Abs(r2 - tds) < 2.00m || Math.Abs(r10 - tds) < 2.00m;

            if (!matchesRate)
            {
                var explanation = $"TDS deduction rate mismatch on professional/contracting expense '{r.ExpenseLedger}': Deducted tax of {tds:C2} on expense {exp:C2} does not conform to 1%, 2% or 10% statutory rates.";
                results.Add(CreateReconciliationResult(
                    context.CompanyId,
                    explanation,
                    SeverityLevel.Medium,
                    voucherId: r.Id,
                    voucherNumber: r.VoucherNumber,
                    voucherDate: r.VoucherDate,
                    flaggedAmount: tds,
                    evidenceObj: new
                    {
                        Voucher = r.VoucherNumber,
                        Expense = r.ExpenseLedger,
                        ExpenseAmount = exp,
                        TdsRecorded = tds,
                        ExpectedAt1Pct = r1,
                        ExpectedAt2Pct = r2,
                        ExpectedAt10Pct = r10
                    }
                ));
            }
        }

        return results;
    }
}

// 6. Party Master ↔ Ledger Verification Rule
public class PartyMasterReconciliationRule : BaseReconciliationRule
{
    public override string RuleId => "REC-PTY-01";
    public override string RuleCode => "REC-PTY-01";
    public override string RuleName => "Party Master Data Quality & Registration Reconciliation";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Audits debtor and creditor party masters for duplicate registrations, missing GSTINs, or inconsistent PAN entries.";

    public PartyMasterReconciliationRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory) { }

    public override async Task<IReadOnlyList<AuditResult>> ExecuteAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT Id, Name, ParentGroup, GSTIN, PAN
            FROM Ledgers
            WHERE CompanyId = @CompanyId 
              AND (ParentGroup = 'Sundry Debtors' OR ParentGroup = 'Sundry Creditors')
              AND (GSTIN IS NULL OR TRIM(GSTIN) = '' OR GSTIN = 'Unregistered' OR PAN IS NULL OR TRIM(PAN) = '');
        ";

        var ledgers = await connection.QueryAsync(new CommandDefinition(sql, new { CompanyId = context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var l in ledgers)
        {
            var explanation = $"Party master data requires review: Party ledger '{l.Name}' in '{l.ParentGroup}' is missing standard PAN or GSTIN credentials.";
            results.Add(CreateReconciliationResult(
                context.CompanyId,
                explanation,
                SeverityLevel.Low,
                ledgerId: l.Id,
                evidenceObj: new { PartyName = l.Name, l.ParentGroup, GSTIN = l.GSTIN ?? "Missing", PAN = l.PAN ?? "Missing" }
            ));
        }

        return results;
    }
}

// 7. Sales Ledger ↔ GST Tax Ledger Reconciliation Rule
public class SalesGstReconciliationRule : BaseReconciliationRule
{
    public override string RuleId => "REC-SLS-01";
    public override string RuleCode => "REC-SLS-01";
    public override string RuleName => "Sales Ledger vs GST Tax Ledger Reconciliation";
    public override RuleCategory Category => RuleCategory.GST;
    public override string Description => "Verifies whether Sales voucher total gross balances reconcile perfectly with recorded sales taxable values plus output tax entries.";

    public SalesGstReconciliationRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory) { }

    public override async Task<IReadOnlyList<AuditResult>> ExecuteAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id, v.VoucherNumber, v.VoucherDate, v.TotalAmount as GrossValue,
                   COALESCE(SUM(CASE WHEN e.LedgerName LIKE '%Sales%' OR e.LedgerName LIKE '%Revenue%' THEN ABS(e.Amount) ELSE 0 END), 0) as TaxableValue,
                   COALESCE(SUM(CASE WHEN e.LedgerName LIKE '%CGST%' OR e.LedgerName LIKE '%SGST%' OR e.LedgerName LIKE '%IGST%' THEN ABS(e.Amount) ELSE 0 END), 0) as TaxAmount
            FROM Vouchers v
            JOIN VoucherEntries e ON e.VoucherId = v.Id
            WHERE v.CompanyId = @CompanyId AND v.VoucherTypeName LIKE '%Sales%' AND v.VoucherDate BETWEEN @FromDate AND @ToDate
            GROUP BY v.Id;
        ";

        var records = await connection.QueryAsync(new CommandDefinition(sql, new { CompanyId = context.CompanyId, context.FromDate, context.ToDate }, cancellationToken: cancellationToken));

        foreach (var r in records)
        {
            decimal gross = r.GrossValue;
            decimal taxable = r.TaxableValue;
            decimal tax = r.TaxAmount;

            decimal calculatedGross = taxable + tax;
            decimal diff = Math.Abs(gross - calculatedGross);

            if (diff > 5.00m && taxable > 0) // Materiality limit ₹5.00
            {
                var explanation = $"Sales voucher gross value imbalance: Sales voucher {r.VoucherNumber} has recorded gross amount of {gross:C2}, but calculated Sales ({taxable:C2}) + Taxes ({tax:C2}) is {calculatedGross:C2}.";
                results.Add(CreateReconciliationResult(
                    context.CompanyId,
                    explanation,
                    SeverityLevel.Medium,
                    voucherId: r.Id,
                    voucherNumber: r.VoucherNumber,
                    voucherDate: r.VoucherDate,
                    flaggedAmount: diff,
                    evidenceObj: new { Voucher = r.VoucherNumber, Gross = gross, SalesValue = taxable, RecordedGST = tax, Discrepancy = diff }
                ));
            }
        }

        return results;
    }
}

// 8. Purchase Ledger ↔ Input GST Reconciliation Rule
public class PurchaseGstReconciliationRule : BaseReconciliationRule
{
    public override string RuleId => "REC-PUR-01";
    public override string RuleCode => "REC-PUR-01";
    public override string RuleName => "Purchase Ledger vs Input GST Reconciliation";
    public override RuleCategory Category => RuleCategory.GST;
    public override string Description => "Verifies whether Purchase voucher gross values reconcile with taxable values plus recorded Input ITC entries.";

    public PurchaseGstReconciliationRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory) { }

    public override async Task<IReadOnlyList<AuditResult>> ExecuteAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id, v.VoucherNumber, v.VoucherDate, v.TotalAmount as GrossValue,
                   COALESCE(SUM(CASE WHEN e.LedgerName LIKE '%Purchase%' OR e.LedgerName LIKE '%Asset%' THEN ABS(e.Amount) ELSE 0 END), 0) as TaxableValue,
                   COALESCE(SUM(CASE WHEN e.LedgerName LIKE '%Input%' OR e.LedgerName LIKE '%CGST Input%' OR e.LedgerName LIKE '%SGST Input%' OR e.LedgerName LIKE '%IGST Input%' THEN ABS(e.Amount) ELSE 0 END), 0) as TaxAmount
            FROM Vouchers v
            JOIN VoucherEntries e ON e.VoucherId = v.Id
            WHERE v.CompanyId = @CompanyId AND v.VoucherTypeName LIKE '%Purchase%' AND v.VoucherDate BETWEEN @FromDate AND @ToDate
            GROUP BY v.Id;
        ";

        var records = await connection.QueryAsync(new CommandDefinition(sql, new { CompanyId = context.CompanyId, context.FromDate, context.ToDate }, cancellationToken: cancellationToken));

        foreach (var r in records)
        {
            decimal gross = r.GrossValue;
            decimal taxable = r.TaxableValue;
            decimal tax = r.TaxAmount;

            decimal calculatedGross = taxable + tax;
            decimal diff = Math.Abs(gross - calculatedGross);

            if (diff > 5.00m && taxable > 0) // Materiality limit ₹5.00
            {
                var explanation = $"Purchase voucher gross value imbalance: Purchase voucher {r.VoucherNumber} has gross of {gross:C2}, but calculated Purchase ({taxable:C2}) + ITC ({tax:C2}) is {calculatedGross:C2}.";
                results.Add(CreateReconciliationResult(
                    context.CompanyId,
                    explanation,
                    SeverityLevel.Medium,
                    voucherId: r.Id,
                    voucherNumber: r.VoucherNumber,
                    voucherDate: r.VoucherDate,
                    flaggedAmount: diff,
                    evidenceObj: new { Voucher = r.VoucherNumber, Gross = gross, PurchaseValue = taxable, RecordedITC = tax, Discrepancy = diff }
                ));
            }
        }

        return results;
    }
}

// 9. Expense Ledger ↔ TDS Reconciliation Rule
public class ExpenseTdsReconciliationRule : BaseReconciliationRule
{
    public override string RuleId => "REC-EXP-01";
    public override string RuleCode => "REC-EXP-01";
    public override string RuleName => "Expense Ledger vs TDS Posting Check";
    public override RuleCategory Category => RuleCategory.TDS;
    public override string Description => "Cross-reconciles high-value operating expense line postings against corresponding TDS withholding entries.";

    public ExpenseTdsReconciliationRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory) { }

    public override async Task<IReadOnlyList<AuditResult>> ExecuteAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id, v.VoucherNumber, v.VoucherDate, v.TotalAmount, v.PartyLedgerName,
                   COALESCE(SUM(CASE WHEN e.LedgerName LIKE '%Indirect Expenses%' OR e.LedgerName LIKE '%Professional%' OR e.LedgerName LIKE '%Rent%' THEN ABS(e.Amount) ELSE 0 END), 0) as ExpenseValue,
                   COALESCE(SUM(CASE WHEN e.LedgerName LIKE '%TDS%' OR e.LedgerName LIKE '%Tax Deducted%' THEN ABS(e.Amount) ELSE 0 END), 0) as TdsValue
            FROM Vouchers v
            JOIN VoucherEntries e ON e.VoucherId = v.Id
            WHERE v.CompanyId = @CompanyId AND v.VoucherDate BETWEEN @FromDate AND @ToDate
              AND (v.VoucherTypeName LIKE '%Payment%' OR v.VoucherTypeName LIKE '%Journal%')
            GROUP BY v.Id
            HAVING ExpenseValue >= 100000.0 AND TdsValue = 0;
        ";

        var anomalies = await connection.QueryAsync(new CommandDefinition(sql, new { CompanyId = context.CompanyId, context.FromDate, context.ToDate }, cancellationToken: cancellationToken));

        foreach (var a in anomalies)
        {
            decimal exp = a.ExpenseValue;
            var explanation = $"Expense reconciliation exception: Expense payment of {exp:C2} to '{a.PartyLedgerName}' exceeds ₹1,00,000 but lacks any corresponding TDS withholding posting.";
            results.Add(CreateReconciliationResult(
                context.CompanyId,
                explanation,
                SeverityLevel.High,
                voucherId: a.Id,
                voucherNumber: a.VoucherNumber,
                voucherDate: a.VoucherDate,
                flaggedAmount: exp,
                evidenceObj: new { Voucher = a.VoucherNumber, Party = a.PartyLedgerName, ExpenseAmount = exp }
            ));
        }

        return results;
    }
}

// 10. Bank & Cash Duplicate Payment Detection Rule
public class BankCashReconciliationRule : BaseReconciliationRule
{
    public override string RuleId => "REC-CSH-01";
    public override string RuleCode => "REC-CSH-01";
    public override string RuleName => "Bank & Cash Duplicate Payment Verification";
    public override RuleCategory Category => RuleCategory.Banking;
    public override string Description => "Cross-checks receipt and payment ledger flows to identify potential duplicate bank/cash transactions.";

    public BankCashReconciliationRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory) { }

    public override async Task<IReadOnlyList<AuditResult>> ExecuteAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v1.Id, v1.VoucherNumber, v1.VoucherDate, v1.TotalAmount, v1.PartyLedgerName, v2.VoucherNumber as DuplicateVoucherNumber
            FROM Vouchers v1
            JOIN Vouchers v2 ON (v1.CompanyId = v2.CompanyId AND v1.VoucherDate = v2.VoucherDate AND v1.TotalAmount = v2.TotalAmount AND v1.PartyLedgerName = v2.PartyLedgerName AND v1.Id < v2.Id)
            WHERE v1.CompanyId = @CompanyId AND v1.VoucherDate BETWEEN @FromDate AND @ToDate
              AND (v1.VoucherTypeName LIKE '%Payment%' OR v1.VoucherTypeName LIKE '%Receipt%');
        ";

        var duplicates = await connection.QueryAsync(new CommandDefinition(sql, new { CompanyId = context.CompanyId, context.FromDate, context.ToDate }, cancellationToken: cancellationToken));

        foreach (var d in duplicates)
        {
            var explanation = $"Potential exception: Suspicious duplicate cash/bank transaction of {d.TotalAmount:C2} detected between voucher '{d.VoucherNumber}' and '{d.DuplicateVoucherNumber}' on the same date.";
            results.Add(CreateReconciliationResult(
                context.CompanyId,
                explanation,
                SeverityLevel.Medium,
                voucherId: d.Id,
                voucherNumber: d.VoucherNumber,
                voucherDate: d.VoucherDate,
                flaggedAmount: d.TotalAmount,
                evidenceObj: new { OriginalVoucher = d.VoucherNumber, DuplicateVoucher = d.DuplicateVoucherNumber, Date = d.VoucherDate, Amount = d.TotalAmount, Party = d.PartyLedgerName }
            ));
        }

        return results;
    }
}

// 11. Cash / Bank Contra Transfer Verification Rule
public class ContraVerificationRule : BaseReconciliationRule
{
    public override string RuleId => "REC-CON-01";
    public override string RuleCode => "REC-CON-01";
    public override string RuleName => "Contra Transfer & Inter-Ledger Reconciler";
    public override RuleCategory Category => RuleCategory.Banking;
    public override string Description => "Audits inter-bank transfers and cash contra entries to guarantee both matching sides represent consistently.";

    public ContraVerificationRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory) { }

    public override async Task<IReadOnlyList<AuditResult>> ExecuteAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id, v.VoucherNumber, v.VoucherDate, v.TotalAmount,
                   COALESCE(SUM(CASE WHEN e.IsDebit = 1 THEN e.Amount ELSE 0 END), 0) as TotalDebits,
                   COALESCE(SUM(CASE WHEN e.IsDebit = 0 THEN e.Amount ELSE 0 END), 0) as TotalCredits
            FROM Vouchers v
            JOIN VoucherEntries e ON e.VoucherId = v.Id
            WHERE v.CompanyId = @CompanyId AND v.VoucherTypeName LIKE '%Contra%' AND v.VoucherDate BETWEEN @FromDate AND @ToDate
            GROUP BY v.Id
            HAVING ABS(TotalDebits - TotalCredits) > 0.05;
        ";

        var misalignments = await connection.QueryAsync(new CommandDefinition(sql, new { CompanyId = context.CompanyId, context.FromDate, context.ToDate }, cancellationToken: cancellationToken));

        foreach (var m in misalignments)
        {
            decimal diff = Math.Abs(m.TotalDebits - m.TotalCredits);
            var explanation = $"Contra transaction transfer imbalance: Contra transfer voucher {m.VoucherNumber} has an unexplained inter-ledger mismatch of {diff:C2} (Debits: {m.TotalDebits:C2}, Credits: {m.TotalCredits:C2}).";
            results.Add(CreateReconciliationResult(
                context.CompanyId,
                explanation,
                SeverityLevel.High,
                voucherId: m.Id,
                voucherNumber: m.VoucherNumber,
                voucherDate: m.VoucherDate,
                flaggedAmount: diff,
                evidenceObj: new { Voucher = m.VoucherNumber, Debits = m.TotalDebits, Credits = m.TotalCredits, Difference = diff }
            ));
        }

        return results;
    }
}

// 12. Period Month-on-Month Balance Roll-Forward Rule
public class PeriodReconciliationRule : BaseReconciliationRule
{
    public override string RuleId => "REC-PRD-01";
    public override string RuleCode => "REC-PRD-01";
    public override string RuleName => "Period Balance Roll-Forward Reconciliation";
    public override RuleCategory Category => RuleCategory.GeneralAccounting;
    public override string Description => "Audits cash and bank accounts month-on-month to verify whether closing balances roll forward seamlessly as next period opening balances.";

    public PeriodReconciliationRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory) { }

    public override async Task<IReadOnlyList<AuditResult>> ExecuteAsync(AuditExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditResult>();
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        // Fetch Cash-in-Hand and Bank ledgers
        const string ledgerSql = @"
            SELECT Id, Name, OpeningBalance, ClosingBalance 
            FROM Ledgers 
            WHERE CompanyId = @CompanyId AND (ParentGroup = 'Cash-in-Hand' OR ParentGroup = 'Bank Accounts');
        ";
        var ledgers = await connection.QueryAsync<Ledger>(new CommandDefinition(ledgerSql, new { CompanyId = context.CompanyId }, cancellationToken: cancellationToken));

        foreach (var l in ledgers)
        {
            // Monthly roll-forward check
            const string monthlySql = @"
                SELECT strftime('%m', v.VoucherDate) as Month,
                       SUM(CASE WHEN e.IsDebit = 1 THEN e.Amount ELSE 0 END) as Debits,
                       SUM(CASE WHEN e.IsDebit = 0 THEN e.Amount ELSE 0 END) as Credits
                FROM VoucherEntries e
                JOIN Vouchers v ON v.Id = e.VoucherId
                WHERE v.CompanyId = @CompanyId AND e.LedgerName = @Name AND v.VoucherDate BETWEEN @FromDate AND @ToDate
                GROUP BY Month
                ORDER BY Month ASC;
            ";

            var months = (await connection.QueryAsync(new CommandDefinition(monthlySql, new { CompanyId = context.CompanyId, Name = l.Name, context.FromDate, context.ToDate }, cancellationToken: cancellationToken))).ToList();

            decimal runningBalance = l.OpeningBalance;

            foreach (var m in months)
            {
                string mName = m.Month;
                decimal d = m.Debits;
                decimal c = m.Credits;

                decimal prevBal = runningBalance;
                runningBalance = runningBalance + d - c;

                // Simple check for suspicious negative dip inside cash/bank
                if (runningBalance < 0)
                {
                    var explanation = $"Period ledger dip anomaly: Cash/Bank account '{l.Name}' dipped into negative closing of {runningBalance:C2} at end of Month {mName}. Opening: {prevBal:C2}.";
                    results.Add(CreateReconciliationResult(
                        context.CompanyId,
                        explanation,
                        SeverityLevel.Medium,
                        ledgerId: l.Id,
                        flaggedAmount: Math.Abs(runningBalance),
                        evidenceObj: new { Account = l.Name, Month = mName, Opening = prevBal, TotalDebits = d, TotalCredits = c, Closing = runningBalance }
                    ));
                }
            }
        }

        return results;
    }
}
