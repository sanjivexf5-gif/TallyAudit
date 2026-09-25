using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Tds;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Tds.Rules;

// 4. TDS Ledger Mapping & Chart of Accounts Classification Rule
public class TdsLedgerMappingRule : BaseTdsRule
{
    public override string RuleId => "TDS-CHK-04";
    public override string Name => "TDS Duties & Taxes Ledger Chart Mapping Check";
    public override string Section => "Accounting Classification";
    public override string Description => "Ensures that all withholding tax ledger heads are correctly parented under 'Duties & Taxes' (or statutory liability sub-groups) and not misclassified as direct/indirect expenses.";
    public override string SourceReference => "Guidance Note on Tax Audit under Section 44AB / ICAI Accounting Standards";
    public override SeverityLevel Severity { get; set; } = SeverityLevel.Medium;

    public TdsLedgerMappingRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT Id, Name, ParentGroup, ClosingBalance
            FROM Ledgers
            WHERE CompanyId = @CompanyId
              AND (Name LIKE '%TDS%' OR Name LIKE '%Tax Deducted%' OR Name LIKE '%Withholding%')
              AND (ParentGroup NOT LIKE '%Duties%' AND ParentGroup NOT LIKE '%Taxes%' AND ParentGroup NOT LIKE '%Provisions%' AND ParentGroup NOT LIKE '%Current Liabilities%');
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));
        var results = new List<TdsCheckResult>();

        foreach (var r in rows)
        {
            results.Add(CreateResult(
                context.CompanyId,
                "TDS withholding ledger '" + (string)r.Name + "' is mapped under group '" + (string?)r.ParentGroup + "' instead of the standard statutory 'Duties & Taxes' or 'Current Liabilities' parent hierarchy.",
                Severity,
                TdsCheckStatus.Exception,
                partyLedgerId: (string)r.Id,
                partyLedgerName: (string)r.Name,
                transactionAmount: (decimal)r.ClosingBalance,
                evidence: new { Ledger = (string)r.Name, CurrentParent = (string?)r.ParentGroup, ExpectedParent = "Duties & Taxes" }
            ));
        }

        return results;
    }
}

// 5. Deduction Amount Consistency Rule (Rate * Base vs Posted Amount)
public class DeductionAmountConsistencyRule : BaseTdsRule
{
    public override string RuleId => "TDS-CHK-05";
    public override string Name => "TDS Deduction Math & Rate-Base Consistency Check";
    public override string Section => "General Math Validation";
    public override string Description => "Recalculates the exact mathematical TDS deduction (Applicable Rate × Taxable Base Value) and flags vouchers where the posted deduction differs beyond rounding tolerances.";
    public override string SourceReference => "Income Tax Act 1961 Chapter XVII-B Calculation Standards";
    public override SeverityLevel Severity { get; set; } = SeverityLevel.High;

    public DeductionAmountConsistencyRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory)
    {
        Parameters = new Dictionary<string, object>
        {
            { "ToleranceRupees", 5.0m }
        };
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var tolerance = GetParam("ToleranceRupees", 5.0m);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName, v.PartyLedgerName,
                   exp.LedgerName as ExpenseHead, exp.Amount as BaseAmount,
                   tds.LedgerName as TdsHead, ABS(tds.Amount) as ActualTds, l.TdsRate, l.PAN as PartyPan
            FROM Vouchers v
            JOIN VoucherEntries exp ON (exp.VoucherId = v.Id AND exp.IsDebit = 1)
            JOIN VoucherEntries tds ON (tds.VoucherId = v.Id AND (tds.LedgerName LIKE '%TDS%' OR tds.LedgerName LIKE '%Tax Deducted%'))
            LEFT JOIN Ledgers l ON (l.CompanyId = v.CompanyId AND l.Name = tds.LedgerName)
            WHERE v.CompanyId = @CompanyId;
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));
        var results = new List<TdsCheckResult>();

        foreach (var r in rows)
        {
            decimal baseAmount = (decimal)r.BaseAmount;
            decimal actualTds = (decimal)r.ActualTds;
            decimal? configuredRate = (decimal?)r.TdsRate;

            if (configuredRate == null || configuredRate == 0)
            {
                // Try extracting from name e.g. "TDS on Contract @ 2%" or "TDS @ 10%"
                string tdsName = ((string?)r.TdsHead) ?? "";
                if (tdsName.Contains("10%")) configuredRate = 10.0m;
                else if (tdsName.Contains("2%")) configuredRate = 2.0m;
                else if (tdsName.Contains("1%")) configuredRate = 1.0m;
                else if (tdsName.Contains("5%")) configuredRate = 5.0m;
                else if (tdsName.Contains("20%")) configuredRate = 20.0m;
            }

            if (configuredRate.HasValue && configuredRate.Value > 0)
            {
                decimal expectedTds = (baseAmount * configuredRate.Value) / 100m;
                decimal diff = Math.Abs(actualTds - expectedTds);

                if (diff > tolerance)
                {
                    results.Add(CreateResult(
                        context.CompanyId,
                        "Mathematical discrepancy detected in voucher " + (string)r.VoucherNumber + ": Base expense " + baseAmount.ToString("C2") + " at rate " + configuredRate.Value + "% yields expected TDS of " + expectedTds.ToString("C2") + ", but actual deduction posted was " + actualTds.ToString("C2") + " (Difference: " + diff.ToString("C2") + ").",
                        Severity,
                        TdsCheckStatus.Exception,
                        voucherId: (string)r.VoucherId,
                        voucherNumber: (string)r.VoucherNumber,
                        voucherDate: (DateTime)r.VoucherDate,
                        voucherTypeName: (string)r.VoucherTypeName,
                        partyLedgerName: (string?)r.PartyLedgerName,
                        partyPan: (string?)r.PartyPan,
                        expenseLedgerName: (string?)r.ExpenseHead,
                        transactionAmount: baseAmount,
                        deductedTdsAmount: actualTds,
                        expectedTdsAmount: expectedTds,
                        appliedRate: (actualTds / (baseAmount > 0 ? baseAmount : 1)) * 100,
                        expectedRate: configuredRate.Value,
                        evidence: new { BaseAmount = baseAmount, ExpectedTds = expectedTds, ActualTds = actualTds, Rate = configuredRate.Value }
                    ));
                }
            }
        }

        return results;
    }
}

// 6. TDS Section Classification Mismatch Rule
public class TdsSectionClassificationRule : BaseTdsRule
{
    public override string RuleId => "TDS-CHK-06";
    public override string Name => "Expense Type vs TDS Section Classification Consistency";
    public override string Section => "194C vs 194J vs 194I";
    public override string Description => "Cross-checks expense classifications against applied TDS heads (e.g., Professional Fees deducted under 194C Contractor at 1%/2% instead of 194J Technical/Professional at 10%).";
    public override string SourceReference => "Income Tax Act 1961 Sec 194C vs Sec 194J Scope of Works";
    public override SeverityLevel Severity { get; set; } = SeverityLevel.High;

    public TdsSectionClassificationRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName, v.PartyLedgerName,
                   exp.LedgerName as ExpenseHead, exp.Amount as BaseAmount,
                   tds.LedgerName as TdsHead, ABS(tds.Amount) as ActualTds, l.PAN as PartyPan
            FROM Vouchers v
            JOIN VoucherEntries exp ON (exp.VoucherId = v.Id AND exp.IsDebit = 1)
            JOIN VoucherEntries tds ON (tds.VoucherId = v.Id AND (tds.LedgerName LIKE '%TDS%' OR tds.LedgerName LIKE '%Tax Deducted%'))
            LEFT JOIN Ledgers l ON (l.CompanyId = v.CompanyId AND l.Name = v.PartyLedgerName)
            WHERE v.CompanyId = @CompanyId;
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));
        var results = new List<TdsCheckResult>();

        foreach (var r in rows)
        {
            string expense = ((string?)r.ExpenseHead) ?? "";
            string tdsHead = ((string?)r.TdsHead) ?? "";

            bool isProfessionalExpense = expense.Contains("Professional", StringComparison.OrdinalIgnoreCase) || expense.Contains("Legal", StringComparison.OrdinalIgnoreCase) || expense.Contains("Consultancy", StringComparison.OrdinalIgnoreCase);
            bool isContractorTds = tdsHead.Contains("194C", StringComparison.OrdinalIgnoreCase) || tdsHead.Contains("Contract", StringComparison.OrdinalIgnoreCase);

            if (isProfessionalExpense && isContractorTds)
            {
                results.Add(CreateResult(
                    context.CompanyId,
                    "Possible Section misclassification: Expense head '" + expense + "' (Professional/Technical nature) has withholding tax deducted under '" + tdsHead + "' (Section 194C Contractor) in voucher " + (string)r.VoucherNumber + ". Section 194J generally governs professional fees.",
                    Severity,
                    TdsCheckStatus.Exception,
                    voucherId: (string)r.VoucherId,
                    voucherNumber: (string)r.VoucherNumber,
                    voucherDate: (DateTime)r.VoucherDate,
                    voucherTypeName: (string)r.VoucherTypeName,
                    partyLedgerName: (string?)r.PartyLedgerName,
                    partyPan: (string?)r.PartyPan,
                    expenseLedgerName: expense,
                    transactionAmount: (decimal)r.BaseAmount,
                    deductedTdsAmount: (decimal)r.ActualTds,
                    evidence: new { ExpenseNature = "Professional/Legal", AppliedTdsSection = "194C Contractor", Voucher = (string)r.VoucherNumber }
                ));
            }
        }

        return results;
    }
}

// 8. Expense Category Analysis (Auditing High-Spend Heads Without TDS)
public class ExpenseCategoryAnalysisRule : BaseTdsRule
{
    public override string RuleId => "TDS-CHK-08";
    public override string Name => "Expense Head Category-Wise Annual TDS Audit";
    public override string Section => "Expense Portfolio";
    public override string Description => "Analyzes annual debit turnovers on high-spend expense categories requiring withholding and flags ledger groups with 0% total tax deducted.";
    public override string SourceReference => "Income Tax Act 1961 Section 40(a)(ia) Disallowance of Expenses";
    public override SeverityLevel Severity { get; set; } = SeverityLevel.Medium;

    public ExpenseCategoryAnalysisRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory)
    {
        Parameters = new Dictionary<string, object>
        {
            { "MinCategoryExpense", 150000.0 }
        };
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var minExpense = GetParam("MinCategoryExpense", 150000.0);

        const string sql = @"
            SELECT e.LedgerName as ExpenseHead, SUM(e.Amount) as TotalDebits, COUNT(DISTINCT v.Id) as VoucherCount
            FROM VoucherEntries e
            JOIN Vouchers v ON (v.Id = e.VoucherId AND v.CompanyId = @CompanyId)
            WHERE e.IsDebit = 1 AND (
                e.LedgerName LIKE '%Rent%' OR e.LedgerName LIKE '%Legal%' OR e.LedgerName LIKE '%Consult%' OR
                e.LedgerName LIKE '%Repair%' OR e.LedgerName LIKE '%Transport%' OR e.LedgerName LIKE '%Security%'
            )
            GROUP BY e.LedgerName
            HAVING SUM(e.Amount) >= @MinExpense;
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, MinExpense = minExpense }, cancellationToken: cancellationToken));
        var results = new List<TdsCheckResult>();

        foreach (var r in rows)
        {
            string head = (string)r.ExpenseHead;
            decimal totalDebits = (decimal)r.TotalDebits;

            // Check total TDS deducted on vouchers containing this head
            const string tdsDeductedSql = @"
                SELECT COALESCE(SUM(ABS(tds.Amount)), 0)
                FROM VoucherEntries tds
                JOIN Vouchers v ON (v.Id = tds.VoucherId AND v.CompanyId = @CompanyId)
                WHERE (tds.LedgerName LIKE '%TDS%' OR tds.LedgerName LIKE '%Tax Deducted%')
                  AND v.Id IN (SELECT VoucherId FROM VoucherEntries WHERE LedgerName = @Head);
            ";
            decimal totalTds = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(tdsDeductedSql, new { context.CompanyId, Head = head }, cancellationToken: cancellationToken));

            if (totalTds == 0)
            {
                results.Add(CreateResult(
                    context.CompanyId,
                    "High-spend expense ledger '" + head + "' has accumulated " + totalDebits.ToString("C2") + " across " + (long)r.VoucherCount + " vouchers with zero corresponding TDS deductions posted. Non-deduction may trigger disallowance under Section 40(a)(ia).",
                    Severity,
                    TdsCheckStatus.Exception,
                    expenseLedgerName: head,
                    transactionAmount: totalDebits,
                    deductedTdsAmount: 0,
                    evidence: new { ExpenseHead = head, AnnualDebits = totalDebits, TotalTdsDeducted = 0.0 }
                ));
            }
        }

        return results;
    }
}

// 9. TDS Payable vs Deduction Ledger Posting & Remittance Analysis
public class TdsPayableVsDeductionRule : BaseTdsRule
{
    public override string RuleId => "TDS-CHK-09";
    public override string Name => "TDS Payable Liability vs Government Remittance Verification";
    public override string Section => "Chapter XVII-B / Sec 200(1)";
    public override string Description => "Tracks monthly cumulative credit accumulations in TDS payable ledgers versus challan payments (Challan 281) to identify potential late deposit or unremitted tax liabilities.";
    public override string SourceReference => "Income Tax Act 1961 Sec 200(1) & Rule 30 Time of Payment";
    public override SeverityLevel Severity { get; set; } = SeverityLevel.High;

    public TdsPayableVsDeductionRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory)
    {
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT l.Name as TdsLedger,
                   SUM(CASE WHEN e.IsDebit = 0 THEN ABS(e.Amount) ELSE 0 END) as TotalDeducted,
                   SUM(CASE WHEN e.IsDebit = 1 THEN ABS(e.Amount) ELSE 0 END) as TotalRemitted,
                   l.ClosingBalance
            FROM Ledgers l
            JOIN VoucherEntries e ON (e.LedgerName = l.Name)
            JOIN Vouchers v ON (v.Id = e.VoucherId AND v.CompanyId = l.CompanyId)
            WHERE l.CompanyId = @CompanyId AND (l.Name LIKE '%TDS%' OR l.Name LIKE '%Tax Deducted%')
            GROUP BY l.Name;
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));
        var results = new List<TdsCheckResult>();

        foreach (var r in rows)
        {
            decimal deducted = (decimal)r.TotalDeducted;
            decimal remitted = (decimal)r.TotalRemitted;
            decimal closingBal = (decimal)r.ClosingBalance;

            if (closingBal < 0) // Credit balance in liability
            {
                results.Add(CreateResult(
                    context.CompanyId,
                    "TDS withholding liability ledger '" + (string)r.TdsLedger + "' reflects an outstanding unremitted credit balance of " + Math.Abs(closingBal).ToString("C2") + " (Total Deducted: " + deducted.ToString("C2") + ", Total Remitted: " + remitted.ToString("C2") + "). Section 200(1) requires statutory deposit within 7 days of the following month.",
                    Severity,
                    TdsCheckStatus.Exception,
                    partyLedgerName: (string)r.TdsLedger,
                    deductedTdsAmount: deducted,
                    transactionAmount: Math.Abs(closingBal),
                    evidence: new { TdsLedger = (string)r.TdsLedger, TotalDeductions = deducted, Remittances = remitted, OutstandingBalance = closingBal }
                ));
            }
        }

        return results;
    }
}
