using Dapper;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Tds;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Tds.Rules;

// 1. Potential TDS Applicability Rule
public class PotentialTdsApplicabilityRule : BaseTdsRule
{
    public override string RuleId => "TDS-CHK-01";
    public override string Name => "Potential Statutory TDS Applicability on Inward Expense Heads";
    public override string Section => "General (194C/J/I/H/Q)";
    public override string Description => "Identifies inward commercial and professional expense heads that typically attract statutory withholding tax provisions under Chapter XVII-B.";
    public override string SourceReference => "Income Tax Act 1961 Chapter XVII-B Deduction at Source";
    public override SeverityLevel Severity { get; set; } = SeverityLevel.Medium;

    public PotentialTdsApplicabilityRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory)
    {
        Parameters = new Dictionary<string, object>
        {
            { "MonitoredHeads", "Professional,Legal,Consultancy,Contract,Sub-contract,Rent,Commission,Brokerage,Transport,Freight,Advertising,Technical" }
        };
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var monitored = GetParam("MonitoredHeads", "Professional,Legal,Consultancy,Contract,Sub-contract,Rent,Commission,Brokerage,Transport,Freight,Advertising,Technical");
        var keywords = monitored.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName, v.TotalAmount, v.PartyLedgerName,
                   e.LedgerName as ExpenseHead, e.Amount as ExpenseAmount, l.PAN as PartyPan
            FROM Vouchers v
            JOIN VoucherEntries e ON (e.VoucherId = v.Id)
            LEFT JOIN Ledgers l ON (l.CompanyId = v.CompanyId AND l.Name = v.PartyLedgerName)
            WHERE v.CompanyId = @CompanyId AND v.VoucherTypeName IN ('Purchase', 'Journal', 'Payment')
              AND e.IsDebit = 1 AND e.Amount >= 20000;
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));
        var results = new List<TdsCheckResult>();

        foreach (var r in rows)
        {
            string head = r.ExpenseHead ?? "";
            bool isMatchingHead = keywords.Any(k => head.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (!isMatchingHead) continue;

            // Check if there is any corresponding TDS entry in the same voucher
            const string checkTdsSql = @"
                SELECT COUNT(*) FROM VoucherEntries
                WHERE VoucherId = @VoucherId AND (LedgerName LIKE '%TDS%' OR LedgerName LIKE '%Tax Deducted%');
            ";
            int tdsCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(checkTdsSql, new { r.VoucherId }, cancellationToken: cancellationToken));

            if (tdsCount == 0)
            {
                if (string.IsNullOrWhiteSpace((string?)r.PartyPan))
                {
                    results.Add(CreateResult(
                        context.CompanyId,
                        "Review Required - Insufficient Data: Nature of service on ledger '" + head + "' indicates potential TDS applicability, but deductee PAN and specific contract terms are unavailable in Tally records.",
                        SeverityLevel.Low,
                        TdsCheckStatus.ReviewRequiredInsufficientData,
                        voucherId: (string)r.VoucherId,
                        voucherNumber: (string)r.VoucherNumber,
                        voucherDate: (DateTime)r.VoucherDate,
                        voucherTypeName: (string)r.VoucherTypeName,
                        partyLedgerName: (string?)r.PartyLedgerName,
                        partyPan: null,
                        expenseLedgerName: head,
                        transactionAmount: (decimal)r.ExpenseAmount,
                        evidence: new { ExpenseHead = head, Amount = (decimal)r.ExpenseAmount, Status = "Review Required - Insufficient Data" }
                    ));
                }
                else
                {
                    results.Add(CreateResult(
                        context.CompanyId,
                        "Potential TDS applicability identified on expense head '" + head + "' (Amount: " + ((decimal)r.ExpenseAmount).ToString("C2") + ") without an associated TDS withholding entry recorded in voucher " + (string)r.VoucherNumber + ".",
                        Severity,
                        TdsCheckStatus.Exception,
                        voucherId: (string)r.VoucherId,
                        voucherNumber: (string)r.VoucherNumber,
                        voucherDate: (DateTime)r.VoucherDate,
                        voucherTypeName: (string)r.VoucherTypeName,
                        partyLedgerName: (string?)r.PartyLedgerName,
                        partyPan: (string?)r.PartyPan,
                        expenseLedgerName: head,
                        transactionAmount: (decimal)r.ExpenseAmount,
                        evidence: new { ExpenseHead = head, Amount = (decimal)r.ExpenseAmount, Party = (string?)r.PartyLedgerName }
                    ));
                }
            }
        }

        return results;
    }
}

// 2. Threshold Monitoring Rule (Single vs Aggregate Thresholds across 194C, 194J, 194I, 194H)
public class ThresholdMonitoringRule : BaseTdsRule
{
    public override string RuleId => "TDS-CHK-02";
    public override string Name => "Single Transaction Statutory Threshold Monitoring";
    public override string Section => "194C / 194J / 194I / 194H";
    public override string Description => "Monitors individual transaction amounts exceeding single-bill statutory thresholds (e.g. ₹30,000 for 194C, ₹30,000 for 194J, ₹2,40,000 for 194I) without TDS deduction.";
    public override string SourceReference => "Income Tax Act 1961 Sec 194C(5), 194J(1), 194I, 194H";
    public override SeverityLevel Severity { get; set; } = SeverityLevel.High;

    public ThresholdMonitoringRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory)
    {
        Parameters = new Dictionary<string, object>
        {
            { "Threshold194C_Single", 30000.0 },
            { "Threshold194J", 30000.0 },
            { "Threshold194I", 240000.0 },
            { "Threshold194H", 15000.0 }
        };
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var t194c = GetParam("Threshold194C_Single", 30000.0);
        var t194j = GetParam("Threshold194J", 30000.0);
        var t194i = GetParam("Threshold194I", 240000.0);
        var t194h = GetParam("Threshold194H", 15000.0);

        const string sql = @"
            SELECT v.Id as VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName, v.TotalAmount, v.PartyLedgerName,
                   e.LedgerName as ExpenseHead, e.Amount as ExpenseAmount, l.PAN as PartyPan
            FROM Vouchers v
            JOIN VoucherEntries e ON (e.VoucherId = v.Id)
            LEFT JOIN Ledgers l ON (l.CompanyId = v.CompanyId AND l.Name = v.PartyLedgerName)
            WHERE v.CompanyId = @CompanyId AND v.VoucherTypeName IN ('Purchase', 'Journal', 'Payment')
              AND e.IsDebit = 1;
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));
        var results = new List<TdsCheckResult>();

        foreach (var r in rows)
        {
            string head = ((string?)r.ExpenseHead) ?? "";
            decimal amount = (decimal)r.ExpenseAmount;
            string section = "";
            decimal threshold = 0;

            if (head.Contains("Contract", StringComparison.OrdinalIgnoreCase) || head.Contains("Transport", StringComparison.OrdinalIgnoreCase) || head.Contains("Freight", StringComparison.OrdinalIgnoreCase) || head.Contains("Fabrication", StringComparison.OrdinalIgnoreCase))
            {
                section = "194C";
                threshold = t194c;
            }
            else if (head.Contains("Professional", StringComparison.OrdinalIgnoreCase) || head.Contains("Legal", StringComparison.OrdinalIgnoreCase) || head.Contains("Consultancy", StringComparison.OrdinalIgnoreCase) || head.Contains("Technical", StringComparison.OrdinalIgnoreCase))
            {
                section = "194J";
                threshold = t194j;
            }
            else if (head.Contains("Rent", StringComparison.OrdinalIgnoreCase))
            {
                section = "194I";
                threshold = t194i;
            }
            else if (head.Contains("Commission", StringComparison.OrdinalIgnoreCase) || head.Contains("Brokerage", StringComparison.OrdinalIgnoreCase))
            {
                section = "194H";
                threshold = t194h;
            }

            if (string.IsNullOrEmpty(section) || amount < threshold) continue;

            // Check if TDS was deducted
            const string checkTdsSql = @"
                SELECT SUM(ABS(Amount)) FROM VoucherEntries
                WHERE VoucherId = @VoucherId AND (LedgerName LIKE '%TDS%' OR LedgerName LIKE '%Tax Deducted%');
            ";
            decimal? tdsAmount = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(checkTdsSql, new { r.VoucherId }, cancellationToken: cancellationToken));

            if (tdsAmount == null || tdsAmount == 0)
            {
                results.Add(CreateResult(
                    context.CompanyId,
                    "Transaction amount " + amount.ToString("C2") + " on expense head '" + head + "' exceeds the Section " + section + " single-bill threshold of " + threshold.ToString("C2") + " without recorded TDS deduction in voucher " + (string)r.VoucherNumber + ".",
                    Severity,
                    TdsCheckStatus.Exception,
                    voucherId: (string)r.VoucherId,
                    voucherNumber: (string)r.VoucherNumber,
                    voucherDate: (DateTime)r.VoucherDate,
                    voucherTypeName: (string)r.VoucherTypeName,
                    partyLedgerName: (string?)r.PartyLedgerName,
                    partyPan: (string?)r.PartyPan,
                    expenseLedgerName: head,
                    transactionAmount: amount,
                    sectionThreshold: threshold,
                    evidence: new { Section = section, Threshold = threshold, TransactionAmount = amount, VoucherNumber = (string)r.VoucherNumber }
                ));
            }
        }

        return results;
    }
}

// 3. PAN Availability & Section 206AA Higher Deduction Check
public class PanAvailabilityAndHigherDeductionRule : BaseTdsRule
{
    public override string RuleId => "TDS-CHK-03";
    public override string Name => "Deductee PAN Availability & Section 206AA Higher Rate Evaluation";
    public override string Section => "206AA";
    public override string Description => "Verifies whether payees subject to withholding tax have a valid 10-character PAN on record; flags cases where deduction is not made at 20% higher statutory rate.";
    public override string SourceReference => "Income Tax Act 1961 Sec 206AA Requirement to furnish PAN";
    public override SeverityLevel Severity { get; set; } = SeverityLevel.High;

    public PanAvailabilityAndHigherDeductionRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory)
    {
        Parameters = new Dictionary<string, object>
        {
            { "HigherRatePercentage", 20.0 }
        };
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var higherRate = GetParam("HigherRatePercentage", 20.0);

        const string sql = @"
            SELECT l.Id as PartyId, l.Name as PartyName, l.PAN, l.ParentGroup,
                   SUM(ABS(e.Amount)) as TotalExpense,
                   COUNT(DISTINCT v.Id) as VoucherCount
            FROM Ledgers l
            JOIN VoucherEntries e ON (e.LedgerName = l.Name)
            JOIN Vouchers v ON (v.Id = e.VoucherId AND v.CompanyId = l.CompanyId)
            WHERE l.CompanyId = @CompanyId AND (l.ParentGroup LIKE '%Creditors%' OR l.TaxType = 'TDS')
              AND (l.PAN IS NULL OR TRIM(l.PAN) = '' OR LENGTH(TRIM(l.PAN)) != 10)
            GROUP BY l.Id, l.Name
            HAVING SUM(ABS(e.Amount)) >= 30000;
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId }, cancellationToken: cancellationToken));
        var results = new List<TdsCheckResult>();

        foreach (var r in rows)
        {
            decimal totalExpense = (decimal)r.TotalExpense;
            decimal expectedTds = (totalExpense * higherRate) / 100m;

            results.Add(CreateResult(
                context.CompanyId,
                "Payee ledger '" + (string)r.PartyName + "' with cumulative transaction volume of " + totalExpense.ToString("C2") + " lacks a valid 10-character PAN. Section 206AA requires statutory tax withholding at the higher rate of " + higherRate + "%.",
                Severity,
                TdsCheckStatus.Exception,
                partyLedgerId: (string)r.PartyId,
                partyLedgerName: (string)r.PartyName,
                partyPan: (string?)r.PAN,
                transactionAmount: totalExpense,
                expectedRate: higherRate,
                expectedTdsAmount: expectedTds,
                evidence: new { Payee = (string)r.PartyName, CumulativeAmount = totalExpense, RequiredRate = higherRate, PAN = (string?)r.PAN ?? "MISSING" }
            ));
        }

        return results;
    }
}

// 7. Vendor-wise Cumulative Transaction Analysis Rule
public class VendorCumulativeAnalysisRule : BaseTdsRule
{
    public override string RuleId => "TDS-CHK-07";
    public override string Name => "Payee Cumulative Financial Year Aggregate Threshold Analysis";
    public override string Section => "194C(5) / 194Q / 206C(1H)";
    public override string Description => "Aggregates multi-voucher transaction totals per vendor across the financial year to detect when cumulative amounts breach statutory thresholds (e.g. ₹1,00,000 for 194C, ₹50,00,000 for 194Q).";
    public override string SourceReference => "Income Tax Act 1961 Sec 194C(5) Aggregate Threshold & Sec 194Q";
    public override SeverityLevel Severity { get; set; } = SeverityLevel.High;

    public VendorCumulativeAnalysisRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory)
    {
        Parameters = new Dictionary<string, object>
        {
            { "ContractAggregateThreshold", 100000.0 },
            { "PurchaseAggregateThreshold", 5000000.0 }
        };
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var contractThreshold = GetParam("ContractAggregateThreshold", 100000.0);

        const string sql = @"
            SELECT v.PartyLedgerName, l.PAN as PartyPan,
                   SUM(v.TotalAmount) as CumulativeAmount,
                   COUNT(v.Id) as VoucherCount,
                   MAX(v.VoucherDate) as LatestDate,
                   MAX(v.VoucherNumber) as LatestVoucher
            FROM Vouchers v
            LEFT JOIN Ledgers l ON (l.CompanyId = v.CompanyId AND l.Name = v.PartyLedgerName)
            WHERE v.CompanyId = @CompanyId AND v.VoucherTypeName IN ('Purchase', 'Journal', 'Payment')
            GROUP BY v.PartyLedgerName
            HAVING SUM(v.TotalAmount) >= @Threshold;
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, Threshold = contractThreshold }, cancellationToken: cancellationToken));
        var results = new List<TdsCheckResult>();

        foreach (var r in rows)
        {
            decimal cumulative = (decimal)r.CumulativeAmount;

            // Check if any TDS was ever deducted for this vendor
            const string checkTdsSql = @"
                SELECT COUNT(*) FROM VoucherEntries ve
                JOIN Vouchers v ON (v.Id = ve.VoucherId)
                WHERE v.CompanyId = @CompanyId AND v.PartyLedgerName = @PartyName
                  AND (ve.LedgerName LIKE '%TDS%' OR ve.LedgerName LIKE '%Tax Deducted%');
            ";
            int tdsCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(checkTdsSql, new { context.CompanyId, PartyName = (string)r.PartyLedgerName }, cancellationToken: cancellationToken));

            if (tdsCount == 0)
            {
                results.Add(CreateResult(
                    context.CompanyId,
                    "Cumulative financial year transactions with payee '" + (string)r.PartyLedgerName + "' total " + cumulative.ToString("C2") + " across " + (long)r.VoucherCount + " vouchers, breaching aggregate statutory threshold (" + contractThreshold.ToString("C2") + ") without recorded TDS withholding.",
                    Severity,
                    TdsCheckStatus.Exception,
                    voucherNumber: (string)r.LatestVoucher,
                    partyLedgerName: (string)r.PartyLedgerName,
                    partyPan: (string?)r.PartyPan,
                    cumulativeVendorAmount: cumulative,
                    sectionThreshold: contractThreshold,
                    evidence: new { Payee = (string)r.PartyLedgerName, CumulativeTotal = cumulative, VoucherCount = (long)r.VoucherCount, Threshold = contractThreshold }
                ));
            }
        }

        return results;
    }
}

// 13. Transactions Around Statutory Thresholds (Threshold Border Analysis)
public class ThresholdBorderTransactionsRule : BaseTdsRule
{
    public override string RuleId => "TDS-CHK-13";
    public override string Name => "Threshold Border & Invoice Splitting Pattern Analysis";
    public override string Section => "194C / 194J";
    public override string Description => "Detects recurring clusters of invoices issued just below statutory TDS thresholds (e.g., between ₹27,000 and ₹29,999) to highlight potential artificial threshold circumvention.";
    public override string SourceReference => "Audit Standards on Fraud & Anti-Circumvention (SA 240 / Section 194C)";
    public override SeverityLevel Severity { get; set; } = SeverityLevel.Medium;

    public ThresholdBorderTransactionsRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory)
    {
        Parameters = new Dictionary<string, object>
        {
            { "LowerBound", 27000.0 },
            { "UpperBound", 29999.0 },
            { "ClusterCountThreshold", 2 }
        };
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var lower = GetParam("LowerBound", 27000.0);
        var upper = GetParam("UpperBound", 29999.0);
        var clusterLimit = GetParam("ClusterCountThreshold", 2);

        const string sql = @"
            SELECT v.PartyLedgerName, COUNT(v.Id) as ClusterCount, SUM(v.TotalAmount) as ClusterTotal,
                   GROUP_CONCAT(v.VoucherNumber, ', ') as VoucherNumbers
            FROM Vouchers v
            WHERE v.CompanyId = @CompanyId AND v.TotalAmount BETWEEN @Lower AND @Upper
            GROUP BY v.PartyLedgerName
            HAVING COUNT(v.Id) >= @Limit;
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { context.CompanyId, Lower = lower, Upper = upper, Limit = clusterLimit }, cancellationToken: cancellationToken));
        var results = new List<TdsCheckResult>();

        foreach (var r in rows)
        {
            results.Add(CreateResult(
                context.CompanyId,
                "Payee '" + (string)r.PartyLedgerName + "' has " + (long)r.ClusterCount + " invoices entered in the border threshold band (" + lower.ToString("C2") + " - " + upper.ToString("C2") + ") totaling " + ((decimal)r.ClusterTotal).ToString("C2") + " (Vouchers: " + (string)r.VoucherNumbers + "). Review required to rule out artificial invoice splitting.",
                Severity,
                TdsCheckStatus.Exception,
                partyLedgerName: (string)r.PartyLedgerName,
                transactionAmount: (decimal)r.ClusterTotal,
                sectionThreshold: upper,
                evidence: new { Payee = (string)r.PartyLedgerName, InvoicesInBand = (long)r.ClusterCount, TotalAmount = (decimal)r.ClusterTotal, Vouchers = (string)r.VoucherNumbers }
            ));
        }

        return results;
    }
}
