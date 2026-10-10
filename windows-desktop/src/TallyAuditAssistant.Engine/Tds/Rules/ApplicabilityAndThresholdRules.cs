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
              AND e.IsDebit = 1 AND e.Amount >= 20000
              AND DATE(v.VoucherDate) BETWEEN DATE(@FromDate) AND DATE(@ToDate);
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new
        {
            context.CompanyId,
            FromDate = context.FromDate.ToString("yyyy-MM-dd"),
            ToDate = context.ToDate.ToString("yyyy-MM-dd")
        }, cancellationToken: cancellationToken));
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
                        voucherDate: GetDateTime(r, "VoucherDate"),
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
                        voucherDate: GetDateTime(r, "VoucherDate"),
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
    private static readonly DateTime FinanceAct2025EffectiveDate = new(2025, 4, 1);
    private static readonly DateTime IncomeTaxAct2025EffectiveDate = new(2026, 4, 1);

    public override string RuleId => "TDS-CHK-02";
    public override string Name => "TDS Threshold Screening";
    public override string Section => "194C / 194J / 194H / 194I";
    public override string Description =>
        "Date-scoped TDS screening for contractor/professional payments and commission/rent aggregates. Results are review indicators, not a determination of TDS liability.";
    public override string SourceReference =>
        "Income-tax Act, 1961 sections 194C, 194H, 194-I and 194J; Finance Act, 2025 threshold amendments effective 1 April 2025; Income-tax Act, 2025 section 393 framework effective 1 April 2026.";
    public override SeverityLevel Severity { get; set; } = SeverityLevel.High;

    public ThresholdMonitoringRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory)
    {
        // Retain historic keys for older periods. New keys prevent older saved defaults
        // from silently overriding the Finance Act 2025 thresholds in upgraded databases.
        Parameters = new Dictionary<string, object>
        {
            { "Threshold194C_Single", 30000m },
            { "Threshold194J", 30000m },
            { "Threshold194J_From2025", 50000m },
            { "Threshold194H", 15000m },
            { "Threshold194H_From2025", 20000m },
            { "Threshold194I", 240000m },
            { "Threshold194I_Monthly_From2025", 50000m }
        };
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(
        TdsAuditContext context,
        CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT v.Id AS VoucherId, v.VoucherNumber, v.VoucherDate, v.VoucherTypeName,
                   v.PartyLedgerName, l.PAN AS PartyPan,
                   e.LedgerName AS ExpenseHead, ABS(e.Amount) AS ExpenseAmount,
                   CASE WHEN EXISTS (
                       SELECT 1 FROM VoucherEntries t
                       WHERE t.VoucherId = v.Id
                         AND (t.LedgerName LIKE '%TDS%' OR t.LedgerName LIKE '%Tax Deducted%')
                   ) THEN 1 ELSE 0 END AS HasTdsEntry
            FROM Vouchers v
            JOIN VoucherEntries e ON e.VoucherId = v.Id
            LEFT JOIN Ledgers l ON l.CompanyId = v.CompanyId AND l.Name = v.PartyLedgerName
            WHERE v.CompanyId = @CompanyId
              AND v.VoucherTypeName IN ('Purchase', 'Journal', 'Payment')
              AND e.IsDebit = 1
              AND DATE(v.VoucherDate) BETWEEN DATE(@FromDate) AND DATE(@ToDate);
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(
            sql,
            new
            {
                context.CompanyId,
                FromDate = context.FromDate.ToString("yyyy-MM-dd"),
                ToDate = context.ToDate.ToString("yyyy-MM-dd")
            },
            cancellationToken: cancellationToken));

        var expenseLines = new List<ThresholdExpenseLine>();
        foreach (var row in rows)
        {
            DateTime? date = GetDateTime((object)row, "VoucherDate");
            var head = GetString(row, "ExpenseHead") ?? string.Empty;
            var section = ClassifyExpenseHead(head);
            if (!date.HasValue || section == null)
            {
                continue;
            }

            expenseLines.Add(new ThresholdExpenseLine(
                GetString(row, "VoucherId") ?? string.Empty,
                GetString(row, "VoucherNumber"),
                date.Value.Date,
                GetString(row, "VoucherTypeName"),
                GetString(row, "PartyLedgerName"),
                GetString(row, "PartyPan"),
                head,
                GetDecimal(row, "ExpenseAmount"),
                section,
                Convert.ToInt32(GetCol(row, "HasTdsEntry") ?? 0) != 0));
        }

        var results = new List<TdsCheckResult>();

        // Per-payment/credit screens: sum matching expense lines within each voucher first.
        foreach (var group in expenseLines
            .Where(x => x.Section is "194C" or "194J")
            .GroupBy(x => new { x.VoucherId, x.Section }))
        {
            var lines = group.OrderBy(x => x.VoucherDate).ToArray();
            var latest = lines[^1];
            var amount = lines.Sum(x => x.Amount);
            var threshold = group.Key.Section == "194C"
                ? GetParam("Threshold194C_Single", 30000m)
                : latest.VoucherDate >= FinanceAct2025EffectiveDate
                    ? GetParam("Threshold194J_From2025", 50000m)
                    : GetParam("Threshold194J", 30000m);

            if (amount <= threshold || lines.All(x => x.HasTdsEntry))
            {
                continue;
            }

            AddScreeningFinding(
                context, results, lines, group.Key.Section, amount, threshold,
                "single voucher", isAggregate: false);
        }

        // Section 194H uses an annual aggregate and applies only to commission/brokerage expense lines.
        foreach (var group in expenseLines
            .Where(x => x.Section == "194H" && !string.IsNullOrWhiteSpace(x.PartyLedgerName))
            .GroupBy(x => new { Payee = x.PartyLedgerName!, FinancialYearStart = GetFinancialYearStart(x.VoucherDate) }))
        {
            var lines = group.OrderBy(x => x.VoucherDate).ToArray();
            var amount = lines.Sum(x => x.Amount);
            var threshold = group.Key.FinancialYearStart >= FinanceAct2025EffectiveDate
                ? GetParam("Threshold194H_From2025", 20000m)
                : GetParam("Threshold194H", 15000m);

            if (amount <= threshold || lines.All(x => x.HasTdsEntry))
            {
                continue;
            }

            AddScreeningFinding(
                context, results, lines, "194H", amount, threshold,
                $"financial year beginning {group.Key.FinancialYearStart:dd-MMM-yyyy}", isAggregate: true);
        }

        // Section 194-I changed to ₹50,000 per month or part from 1 April 2025.
        // Earlier periods use the ₹2,40,000 financial-year aggregate. Rent is grouped
        // by payee and the applicable period, never compared one invoice at a time.
        foreach (var group in expenseLines
            .Where(x => x.Section == "194I" && !string.IsNullOrWhiteSpace(x.PartyLedgerName))
            .GroupBy(x => new
            {
                Payee = x.PartyLedgerName!,
                IsMonthly = x.VoucherDate >= FinanceAct2025EffectiveDate,
                PeriodKey = x.VoucherDate >= FinanceAct2025EffectiveDate
                    ? x.VoucherDate.ToString("yyyy-MM")
                    : GetFinancialYearStart(x.VoucherDate).ToString("yyyy-MM-dd")
            }))
        {
            var lines = group.OrderBy(x => x.VoucherDate).ToArray();
            var amount = lines.Sum(x => x.Amount);
            var threshold = group.Key.IsMonthly
                ? GetParam("Threshold194I_Monthly_From2025", 50000m)
                : GetParam("Threshold194I", 240000m);

            if (amount <= threshold || lines.All(x => x.HasTdsEntry))
            {
                continue;
            }

            var periodLabel = group.Key.IsMonthly
                ? $"month {group.Key.PeriodKey}"
                : $"financial year beginning {group.Key.PeriodKey}";
            AddScreeningFinding(
                context, results, lines, "194I", amount, threshold, periodLabel, isAggregate: true);
        }

        return results;
    }

    private void AddScreeningFinding(
        TdsAuditContext context,
        ICollection<TdsCheckResult> results,
        IReadOnlyList<ThresholdExpenseLine> lines,
        string section,
        decimal screenedAmount,
        decimal threshold,
        string screenedPeriod,
        bool isAggregate)
    {
        var latest = lines[^1];
        var statutoryReference = latest.VoucherDate >= IncomeTaxAct2025EffectiveDate
            ? "Income-tax Act, 2025 section 393 framework"
            : $"Income-tax Act, 1961 section {section}";
        var missingTdsVoucherCount = lines
            .Where(x => !x.HasTdsEntry)
            .Select(x => x.VoucherId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var explanation =
            $"Potential TDS screening flag under section {section}: {screenedAmount:C2} across {screenedPeriod} exceeds the review threshold of {threshold:C2}. " +
            $"No TDS-ledger line was found in {missingTdsVoucherCount} of {lines.Select(x => x.VoucherId).Distinct(StringComparer.Ordinal).Count()} relevant voucher(s). " +
            "This is not a finding of statutory default: voucher date is used as a proxy, and the app cannot determine whether an earlier credit/payment date, exemption, payer/payee status, tax base, or a separate TDS journal changes the result. Verify before concluding.";

        var voucherNumbers = lines
            .Select(x => x.VoucherNumber)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();
        var evidence = new
        {
            Section = section,
            StatutoryReference = statutoryReference,
            ScreenedPeriod = screenedPeriod,
            ScreenedAmount = screenedAmount,
            Threshold = threshold,
            IsAggregate = isAggregate,
            MissingTdsVoucherCount = missingTdsVoucherCount,
            RelevantVoucherCount = lines.Select(x => x.VoucherId).Distinct(StringComparer.Ordinal).Count(),
            VoucherNumbers = voucherNumbers,
            VoucherDateUsedAsProxyForCreditOrPaymentDate = true,
            SeparateTdsJournalPostingsRequireManualReconciliation = true
        };

        var result = CreateResult(
            context.CompanyId,
            explanation,
            Severity,
            voucherId: latest.VoucherId,
            voucherNumber: latest.VoucherNumber,
            voucherDate: latest.VoucherDate,
            voucherTypeName: latest.VoucherTypeName,
            partyLedgerName: latest.PartyLedgerName,
            partyPan: latest.PartyPan,
            expenseLedgerName: string.Join(" / ", lines.Select(x => x.ExpenseHead).Distinct(StringComparer.Ordinal).Take(5)),
            transactionAmount: screenedAmount,
            cumulativeVendorAmount: isAggregate ? screenedAmount : null,
            sectionThreshold: threshold,
            evidence: evidence);

        result.Section = section;
        results.Add(result);
    }

    private static DateTime GetFinancialYearStart(DateTime date) =>
        new(date.Month >= 4 ? date.Year : date.Year - 1, 4, 1);

    private static string? ClassifyExpenseHead(string head)
    {
        if (head.Contains("Commission", StringComparison.OrdinalIgnoreCase) ||
            head.Contains("Brokerage", StringComparison.OrdinalIgnoreCase))
            return "194H";
        if (head.Contains("Rent", StringComparison.OrdinalIgnoreCase))
            return "194I";
        if (head.Contains("Contract", StringComparison.OrdinalIgnoreCase) ||
            head.Contains("Transport", StringComparison.OrdinalIgnoreCase) ||
            head.Contains("Freight", StringComparison.OrdinalIgnoreCase) ||
            head.Contains("Fabrication", StringComparison.OrdinalIgnoreCase))
            return "194C";
        if (head.Contains("Professional", StringComparison.OrdinalIgnoreCase) ||
            head.Contains("Legal", StringComparison.OrdinalIgnoreCase) ||
            head.Contains("Consultancy", StringComparison.OrdinalIgnoreCase) ||
            head.Contains("Technical", StringComparison.OrdinalIgnoreCase))
            return "194J";
        return null;
    }

    private sealed record ThresholdExpenseLine(
        string VoucherId,
        string? VoucherNumber,
        DateTime VoucherDate,
        string? VoucherTypeName,
        string? PartyLedgerName,
        string? PartyPan,
        string ExpenseHead,
        decimal Amount,
        string Section,
        bool HasTdsEntry);
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
            { "HigherRatePercentage", 20m }
        };
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var higherRate = GetParam("HigherRatePercentage", 20m);

        const string sql = @"
            SELECT l.Id as PartyId, l.Name as PartyName, l.PAN, l.ParentGroup,
                   SUM(ABS(e.Amount)) as TotalExpense,
                   COUNT(DISTINCT v.Id) as VoucherCount
            FROM Ledgers l
            JOIN VoucherEntries e ON (e.LedgerName = l.Name)
            JOIN Vouchers v ON (v.Id = e.VoucherId AND v.CompanyId = l.CompanyId)
            WHERE l.CompanyId = @CompanyId AND (l.ParentGroup LIKE '%Creditors%' OR l.TaxType = 'TDS')
              AND (l.PAN IS NULL OR TRIM(l.PAN) = '' OR LENGTH(TRIM(l.PAN)) != 10)
              AND DATE(v.VoucherDate) BETWEEN DATE(@FromDate) AND DATE(@ToDate)
            GROUP BY l.Id, l.Name
            HAVING SUM(ABS(e.Amount)) >= 30000;
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new
        {
            context.CompanyId,
            FromDate = context.FromDate.ToString("yyyy-MM-dd"),
            ToDate = context.ToDate.ToString("yyyy-MM-dd")
        }, cancellationToken: cancellationToken));
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
    public override string Name => "Payee Aggregate Contractor Payment Threshold Screening";
    public override string Section => "194C(5)";
    public override string Description =>
        "Aggregates contractor/freight expense postings by payee within the selected audit period and flags potential section 194C aggregate threshold breaches for review.";
    public override string SourceReference =>
        "Income-tax Act, 1961 section 194C(5); Income-tax Act, 2025 section 393 framework effective 1 April 2026.";
    public override SeverityLevel Severity { get; set; } = SeverityLevel.High;

    public VendorCumulativeAnalysisRule(SqliteConnectionFactory connectionFactory) : base(connectionFactory)
    {
        Parameters = new Dictionary<string, object>
        {
            { "ContractAggregateThreshold", 100000m }
        };
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(
        TdsAuditContext context,
        CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var contractThreshold = GetParam("ContractAggregateThreshold", 100000m);

        // Fetch only expense lines relevant to contractor screening and perform the
        // decimal threshold comparison in C#. SQLite provider affinity can otherwise
        // make decimal aggregate/HAVING comparisons depend on parameter storage type.
        const string sql = @"
            SELECT v.Id AS VoucherId, v.PartyLedgerName, l.PAN AS PartyPan,
                   v.VoucherDate, v.VoucherNumber, e.LedgerName AS ExpenseHead,
                   ABS(e.Amount) AS ExpenseAmount,
                   CASE WHEN EXISTS (
                       SELECT 1 FROM VoucherEntries t
                       WHERE t.VoucherId = v.Id
                         AND (t.LedgerName LIKE '%TDS%' OR t.LedgerName LIKE '%Tax Deducted%')
                   ) THEN 1 ELSE 0 END AS HasTdsEntry
            FROM Vouchers v
            JOIN VoucherEntries e ON e.VoucherId = v.Id
            LEFT JOIN Ledgers l ON l.CompanyId = v.CompanyId AND l.Name = v.PartyLedgerName
            WHERE v.CompanyId = @CompanyId
              AND v.VoucherTypeName IN ('Purchase', 'Journal', 'Payment')
              AND v.PartyLedgerName IS NOT NULL AND TRIM(v.PartyLedgerName) <> ''
              AND e.IsDebit = 1
              AND (e.LedgerName LIKE '%Contract%'
                   OR e.LedgerName LIKE '%Transport%'
                   OR e.LedgerName LIKE '%Freight%'
                   OR e.LedgerName LIKE '%Fabrication%')
              AND DATE(v.VoucherDate) BETWEEN DATE(@FromDate) AND DATE(@ToDate)
            ORDER BY v.PartyLedgerName, v.VoucherDate;
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(
            sql,
            new
            {
                context.CompanyId,
                FromDate = context.FromDate.ToString("yyyy-MM-dd"),
                ToDate = context.ToDate.ToString("yyyy-MM-dd")
            },
            cancellationToken: cancellationToken));

        var expenseLines = new List<VendorExpenseLine>();
        foreach (var row in rows)
        {
            DateTime? date = GetDateTime((object)row, "VoucherDate");
            var payee = GetString((object)row, "PartyLedgerName");
            var voucherId = GetString((object)row, "VoucherId");
            if (!date.HasValue || string.IsNullOrWhiteSpace(payee) || string.IsNullOrWhiteSpace(voucherId))
            {
                continue;
            }

            expenseLines.Add(new VendorExpenseLine(
                voucherId,
                GetString((object)row, "VoucherNumber"),
                date.Value.Date,
                payee,
                GetString((object)row, "PartyPan"),
                GetDecimal((object)row, "ExpenseAmount"),
                Convert.ToInt32(GetCol((object)row, "HasTdsEntry") ?? 0) != 0));
        }

        var results = new List<TdsCheckResult>();
        foreach (var payeeGroup in expenseLines.GroupBy(x => x.PartyLedgerName, StringComparer.OrdinalIgnoreCase))
        {
            var lines = payeeGroup.OrderBy(x => x.VoucherDate).ToArray();
            var amount = lines.Sum(x => x.Amount);
            if (amount <= contractThreshold)
            {
                continue;
            }

            var distinctVouchers = lines
                .GroupBy(x => x.VoucherId, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToArray();
            var vouchersWithTds = distinctVouchers.Count(x => x.HasTdsEntry);
            if (distinctVouchers.Length == 0 || vouchersWithTds >= distinctVouchers.Length)
            {
                continue;
            }

            var latest = lines[^1];
            var statutoryReference = latest.VoucherDate >= new DateTime(2026, 4, 1)
                ? "Income-tax Act, 2025 section 393 framework"
                : "Income-tax Act, 1961 section 194C(5)";
            var explanation =
                $"Potential section 194C aggregate-threshold screening flag: contractor/freight expense postings for payee '{latest.PartyLedgerName}' total {amount:C2} across {distinctVouchers.Length} voucher(s) in the selected audit period, exceeding {contractThreshold:C2}. " +
                $"A TDS-ledger line was found in {vouchersWithTds} of those vouchers. Separate TDS journals, credit/payment timing, payer/payee applicability and statutory exceptions must be checked before concluding.";

            results.Add(CreateResult(
                context.CompanyId,
                explanation,
                Severity,
                TdsCheckStatus.Exception,
                voucherId: latest.VoucherId,
                voucherNumber: latest.VoucherNumber,
                voucherDate: latest.VoucherDate,
                partyLedgerName: latest.PartyLedgerName,
                partyPan: latest.PartyPan,
                expenseLedgerName: string.Join(" / ", lines.Select(x => x.ExpenseHead).Distinct(StringComparer.Ordinal).Take(5)),
                transactionAmount: amount,
                cumulativeVendorAmount: amount,
                sectionThreshold: contractThreshold,
                evidence: new
                {
                    Section = "194C(5)",
                    StatutoryReference = statutoryReference,
                    Payee = latest.PartyLedgerName,
                    CumulativeContractExpense = amount,
                    VoucherCount = distinctVouchers.Length,
                    VouchersWithTdsEntry = vouchersWithTds,
                    Threshold = contractThreshold,
                    PeriodFrom = context.FromDate.ToString("yyyy-MM-dd"),
                    PeriodTo = context.ToDate.ToString("yyyy-MM-dd"),
                    SeparateTdsJournalPostingsRequireManualReconciliation = true
                }));
        }

        return results;
    }

    private sealed record VendorExpenseLine(
        string VoucherId,
        string? VoucherNumber,
        DateTime VoucherDate,
        string PartyLedgerName,
        string? PartyPan,
        decimal Amount,
        bool HasTdsEntry);
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
            { "LowerBound", 27000m },
            { "UpperBound", 29999m },
            { "ClusterCountThreshold", 2 }
        };
    }

    public override async Task<IReadOnlyList<TdsCheckResult>> EvaluateAsync(TdsAuditContext context, CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var lower = GetParam("LowerBound", 27000m);
        var upper = GetParam("UpperBound", 29999m);
        var clusterLimit = GetParam("ClusterCountThreshold", 2);

        const string sql = @"
            SELECT v.PartyLedgerName, COUNT(v.Id) as ClusterCount, SUM(v.TotalAmount) as ClusterTotal,
                   GROUP_CONCAT(v.VoucherNumber, ', ') as VoucherNumbers
            FROM Vouchers v
            WHERE v.CompanyId = @CompanyId AND v.TotalAmount BETWEEN @Lower AND @Upper
              AND DATE(v.VoucherDate) BETWEEN DATE(@FromDate) AND DATE(@ToDate)
            GROUP BY v.PartyLedgerName
            HAVING COUNT(v.Id) >= @Limit;
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new
        {
            context.CompanyId,
            Lower = lower,
            Upper = upper,
            Limit = clusterLimit,
            FromDate = context.FromDate.ToString("yyyy-MM-dd"),
            ToDate = context.ToDate.ToString("yyyy-MM-dd")
        }, cancellationToken: cancellationToken));
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
