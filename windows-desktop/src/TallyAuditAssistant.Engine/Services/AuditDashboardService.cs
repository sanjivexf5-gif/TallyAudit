using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Engine.Services;

/// <summary>
/// Auditor-focused Audit Dashboard 2.0 service that aggregates audit exceptions, calculates
/// transparent deterministic risk scores, summarizes financial amounts (using decimal only),
/// and produces intelligence metrics across 15 audit areas.
/// Strictly READ ONLY - performs no mutations against TallyPrime or accounting data.
/// </summary>
public class AuditDashboardService : IAuditDashboardService
{
    private readonly IAuditRepository _repository;
    private readonly IActiveCompanyContext? _companyContext;
    private readonly IInvestigationRepository? _investigationRepository;
    private readonly ILogger<AuditDashboardService> _logger;

    private readonly ConcurrentDictionary<string, AuditDashboardSummary> _cache = new(StringComparer.OrdinalIgnoreCase);

    public RiskScoringOptions Options { get; set; } = new();

    public AuditDashboardService(
        IAuditRepository repository,
        IActiveCompanyContext? companyContext = null,
        IInvestigationRepository? investigationRepository = null,
        ILogger<AuditDashboardService>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _companyContext = companyContext;
        _investigationRepository = investigationRepository;
        _logger = logger ?? NullLogger<AuditDashboardService>.Instance;
    }

    public Task InvalidateCacheAsync(string? companyId = null)
    {
        if (string.IsNullOrWhiteSpace(companyId))
        {
            _cache.Clear();
            _logger.LogDebug("[AuditDashboardService] Cleared entire dashboard summary cache.");
        }
        else
        {
            var keysToRemove = _cache.Keys.Where(k => k.StartsWith(companyId + ":", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }
            _logger.LogDebug("[AuditDashboardService] Invalidated dashboard cache for company: {CompanyId}", companyId);
        }
        return Task.CompletedTask;
    }

    public async Task<AuditDashboardSummary> GetDashboardSummaryAsync(
        string companyId,
        DateTime? periodFrom = null,
        DateTime? periodTo = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyId))
        {
            return new AuditDashboardSummary
            {
                StatusMessage = "No Company Selected",
                HasExecutedAudit = false
            };
        }

        var cacheKey = $"{companyId}:{periodFrom:yyyy-MM-dd}:{periodTo:yyyy-MM-dd}";
        if (_cache.TryGetValue(cacheKey, out var cachedSummary))
        {
            return cachedSummary;
        }

        // 1. Fetch exceptions strictly scoped to target company (company isolation)
        var allCompanyExceptions = await _repository.GetExceptionsAsync(
            companyId,
            category: null,
            minSeverity: null,
            status: null,
            skip: 0,
            take: 10000,
            cancellationToken: cancellationToken);

        // Filter by companyId explicitly for bulletproof isolation
        var companyExceptions = allCompanyExceptions
            .Where(e => string.Equals(e.CompanyId, companyId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Period isolation
        var scopedExceptions = companyExceptions.Where(e =>
        {
            if (!e.VoucherDate.HasValue) return true;
            if (periodFrom.HasValue && e.VoucherDate.Value.Date < periodFrom.Value.Date) return false;
            if (periodTo.HasValue && e.VoucherDate.Value.Date > periodTo.Value.Date) return false;
            return true;
        }).ToList();

        // 2. Audit run history to determine if audit has executed
        var auditRuns = await _repository.GetAuditRunsAsync(companyId, cancellationToken);
        bool hasExecutedAudit = auditRuns.Count > 0 || scopedExceptions.Count > 0;

        // 3. Resolve active company details
        string companyName = companyId;
        string financialYear = "FY 2025-26";
        if (_companyContext != null && string.Equals(_companyContext.ActiveCompanyId, companyId, StringComparison.OrdinalIgnoreCase))
        {
            companyName = _companyContext.ActiveCompanyName ?? companyId;
            financialYear = _companyContext.ActiveFinancialYear ?? financialYear;
        }
        else
        {
            var compRecord = await _repository.GetCompanyByIdAsync(companyId, cancellationToken);
            if (compRecord != null)
            {
                companyName = compRecord.TallyCompanyName;
                financialYear = $"FY {compRecord.BooksFromDate.Year}-{(compRecord.BooksFromDate.Year + 1) % 100:D2}";
            }
        }

        // 4. Severity counts
        int criticalCount = scopedExceptions.Count(e => e.Severity == SeverityLevel.Critical);
        int highCount = scopedExceptions.Count(e => e.Severity == SeverityLevel.High);
        int mediumCount = scopedExceptions.Count(e => e.Severity == SeverityLevel.Medium);
        int lowCount = scopedExceptions.Count(e => e.Severity == SeverityLevel.Low);
        int informationalCount = scopedExceptions.Count(e => e.Severity == SeverityLevel.Informational);
        int totalFindings = scopedExceptions.Count;

        // 5. Status counts
        int openFindings = scopedExceptions.Count(e => e.Status == ReviewStatus.Pending || e.Status == ReviewStatus.RequiresClientClarification);
        int reviewedFindings = scopedExceptions.Count(e => e.Status == ReviewStatus.Reviewed);
        int resolvedFindings = scopedExceptions.Count(e => e.Status == ReviewStatus.Resolved || e.Status == ReviewStatus.FlaggedAsFalsePositive);

        // 6. Deterministic Transparent Risk Scoring
        int rawPoints = (criticalCount * Options.CriticalWeight) +
                        (highCount * Options.HighWeight) +
                        (mediumCount * Options.MediumWeight) +
                        (lowCount * Options.LowWeight);

        int overallRiskScore = totalFindings == 0 ? 0 : Math.Clamp((int)Math.Round((decimal)rawPoints * 100m / Options.BenchmarkMaxPoints), 0, 100);

        string overallRiskLevel;
        if (overallRiskScore <= Options.LowMaxThreshold) overallRiskLevel = "Low";
        else if (overallRiskScore <= Options.MediumMaxThreshold) overallRiskLevel = "Medium";
        else if (overallRiskScore <= Options.HighMaxThreshold) overallRiskLevel = "High";
        else overallRiskLevel = "Critical";

        // 7. Financial Amounts (Decimal only, strictly never double)
        decimal totalExceptionAmount = scopedExceptions.Sum(e => e.FlaggedAmount ?? 0m);

        decimal gstExceptionAmount = scopedExceptions
            .Where(IsGstException)
            .Sum(e => e.FlaggedAmount ?? 0m);

        decimal tdsExceptionAmount = scopedExceptions
            .Where(IsTdsException)
            .Sum(e => e.FlaggedAmount ?? 0m);

        decimal duplicateExceptionAmount = scopedExceptions
            .Where(IsDuplicateException)
            .Sum(e => e.FlaggedAmount ?? 0m);

        decimal journalExceptionAmount = scopedExceptions
            .Where(IsJournalException)
            .Sum(e => e.FlaggedAmount ?? 0m);

        decimal salesExceptionAmount = scopedExceptions
            .Where(IsSalesException)
            .Sum(e => e.FlaggedAmount ?? 0m);

        decimal purchaseExceptionAmount = scopedExceptions
            .Where(IsPurchaseException)
            .Sum(e => e.FlaggedAmount ?? 0m);

        decimal expenseExceptionAmount = scopedExceptions
            .Where(IsExpenseException)
            .Sum(e => e.FlaggedAmount ?? 0m);

        // 8. Aggregate 15 Audit Areas
        var auditAreaSummaries = BuildAuditAreaSummaries(scopedExceptions);

        // 9. Top Findings (Ordered by Critical, High, Amount, Risk score)
        var topFindings = await BuildTopFindingsAsync(companyId, scopedExceptions, cancellationToken);

        // Status message
        string statusMessage;
        if (!hasExecutedAudit)
        {
            statusMessage = "No audit results available yet.";
        }
        else if (totalFindings == 0)
        {
            statusMessage = "No exceptions detected for the selected audit scope.";
        }
        else
        {
            statusMessage = $"{totalFindings} audit exceptions identified ({criticalCount} critical, {highCount} high).";
        }

        var summary = new AuditDashboardSummary
        {
            OverallRiskScore = overallRiskScore,
            OverallRiskLevel = overallRiskLevel,
            CriticalCount = criticalCount,
            HighCount = highCount,
            MediumCount = mediumCount,
            LowCount = lowCount,
            InformationalCount = informationalCount,
            TotalFindings = totalFindings,
            OpenFindings = openFindings,
            ReviewedFindings = reviewedFindings,
            ResolvedFindings = resolvedFindings,
            TotalExceptionAmount = totalExceptionAmount,
            GstExceptionAmount = gstExceptionAmount,
            TdsExceptionAmount = tdsExceptionAmount,
            DuplicateExceptionAmount = duplicateExceptionAmount,
            JournalExceptionAmount = journalExceptionAmount,
            SalesExceptionAmount = salesExceptionAmount,
            PurchaseExceptionAmount = purchaseExceptionAmount,
            ExpenseExceptionAmount = expenseExceptionAmount,
            AuditAreaSummaries = auditAreaSummaries,
            TopFindings = topFindings,
            HasExecutedAudit = hasExecutedAudit,
            StatusMessage = statusMessage,
            CompanyId = companyId,
            CompanyName = companyName,
            FinancialYear = financialYear,
            AuditPeriodFrom = periodFrom,
            AuditPeriodTo = periodTo,
            CalculatedAt = DateTime.UtcNow
        };

        _cache[cacheKey] = summary;
        return summary;
    }

    private IReadOnlyList<AuditAreaSummary> BuildAuditAreaSummaries(IReadOnlyList<AuditException> exceptions)
    {
        var areaDefinitions = new (string Name, Func<AuditException, bool> Matcher)[]
        {
            ("GST", IsGstException),
            ("TDS", IsTdsException),
            ("Sales", IsSalesException),
            ("Purchases", IsPurchaseException),
            ("Expenses", IsExpenseException),
            ("Bank", IsBankException),
            ("Cash", IsCashException),
            ("Ledgers", IsLedgerException),
            ("Journals", IsJournalException),
            ("Receivables", IsReceivablesException),
            ("Payables", IsPayablesException),
            ("Related Parties", IsRelatedPartyException),
            ("Period End", IsPeriodEndException),
            ("Master Data", IsMasterDataException),
            ("Reconciliation", IsReconciliationException)
        };

        var summaries = new List<AuditAreaSummary>();

        foreach (var def in areaDefinitions)
        {
            var areaExceptions = exceptions.Where(def.Matcher).ToList();
            if (areaExceptions.Count == 0)
            {
                // Only include areas for which data exists
                continue;
            }

            int critical = areaExceptions.Count(e => e.Severity == SeverityLevel.Critical);
            int high = areaExceptions.Count(e => e.Severity == SeverityLevel.High);
            int medium = areaExceptions.Count(e => e.Severity == SeverityLevel.Medium);
            int low = areaExceptions.Count(e => e.Severity == SeverityLevel.Low);

            int rawAreaPoints = (critical * Options.CriticalWeight) +
                                (high * Options.HighWeight) +
                                (medium * Options.MediumWeight) +
                                (low * Options.LowWeight);

            int areaRiskScore = Math.Clamp((int)Math.Round((decimal)rawAreaPoints * 100m / Options.BenchmarkMaxPoints), 0, 100);

            summaries.Add(new AuditAreaSummary
            {
                AreaName = def.Name,
                FindingCount = areaExceptions.Count,
                CriticalCount = critical,
                HighCount = high,
                MediumCount = medium,
                LowCount = low,
                ExceptionAmount = areaExceptions.Sum(e => e.FlaggedAmount ?? 0m),
                RiskScore = areaRiskScore
            });
        }

        return summaries;
    }

    private async Task<IReadOnlyList<TopFindingItem>> BuildTopFindingsAsync(
        string companyId,
        IReadOnlyList<AuditException> exceptions,
        CancellationToken cancellationToken,
        int limit = 10)
    {
        Dictionary<string, string?> workingPaperMap = new(StringComparer.OrdinalIgnoreCase);
        if (_investigationRepository != null)
        {
            try
            {
                var investigations = await _investigationRepository.GetByCompanyIdAsync(companyId, cancellationToken: cancellationToken);
                foreach (var inv in investigations)
                {
                    if (!string.IsNullOrWhiteSpace(inv.LinkedWorkingPaperIds))
                    {
                        workingPaperMap[inv.ExceptionId] = inv.LinkedWorkingPaperIds;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to load working paper links for top findings");
            }
        }

        var topItems = exceptions.Select(e =>
        {
            int score = GetFindingSeverityWeight(e.Severity);
            workingPaperMap.TryGetValue(e.Id, out var wpId);

            return new TopFindingItem
            {
                FindingId = e.Id,
                Title = !string.IsNullOrWhiteSpace(e.RuleName) ? e.RuleName : e.RuleId,
                Category = e.Category.ToString(),
                Severity = e.Severity,
                Amount = e.FlaggedAmount ?? 0m,
                Description = !string.IsNullOrWhiteSpace(e.SuggestedCorrection) ? e.SuggestedCorrection : e.RuleName,
                SourceReference = !string.IsNullOrWhiteSpace(e.VoucherNumber)
                    ? $"Voucher #{e.VoucherNumber} ({(e.VoucherDate.HasValue ? e.VoucherDate.Value.ToString("dd-MMM-yyyy") : "—")})"
                    : (!string.IsNullOrWhiteSpace(e.LedgerName) ? $"Ledger: {e.LedgerName}" : (e.EntityId ?? "—")),
                RecommendedAuditProcedure = GetRecommendedProcedure(e),
                ReviewStatus = e.Status,
                RiskScore = score,
                LinkedWorkingPaperId = wpId
            };
        })
        .OrderByDescending(f => f.Severity)
        .ThenByDescending(f => f.Amount)
        .ThenByDescending(f => f.RiskScore)
        .Take(limit)
        .ToList();

        return topItems;
    }

    private int GetFindingSeverityWeight(SeverityLevel severity) => severity switch
    {
        SeverityLevel.Critical => Options.CriticalWeight,
        SeverityLevel.High => Options.HighWeight,
        SeverityLevel.Medium => Options.MediumWeight,
        SeverityLevel.Low => Options.LowWeight,
        _ => 0
    };

    private static string GetRecommendedProcedure(AuditException ex)
    {
        if (IsGstException(ex))
            return "Verify tax invoice validity, GSTIN active status on portal, and 2B ITC reconciliation.";
        if (IsTdsException(ex))
            return "Verify Section applicability, PAN validity, threshold deduction and deposit challan.";
        if (IsReconciliationException(ex))
            return "Investigate books vs statutory portal variance, identify timing differences, and rectify mismatch.";
        if (IsDuplicateException(ex))
            return "Inspect underlying physical invoices and party ledgers to confirm if voucher is an erroneous double entry.";
        if (IsCashException(ex))
            return "Verify physical cash book balance, single-day cash receipt limits under Sec 269ST, and approval signatures.";
        if (IsBankException(ex))
            return "Perform bank reconciliation against official bank statement and inspect uncredited items.";
        if (IsJournalException(ex))
            return "Inspect journal voucher narration, supporting approvals, and non-cash justification.";
        if (IsSalesException(ex))
            return "Inspect sales invoice sequence, e-Invoice IRN, and customer ledger confirmation.";
        if (IsPurchaseException(ex))
            return "Inspect vendor invoice, GRN matching, purchase order, and delivery challan.";
        if (IsExpenseException(ex))
            return "Verify business purpose, payment mode compliance under Sec 40A(3), and supporting vouchers.";
        if (IsReceivablesException(ex))
            return "Send debtor balance confirmations, inspect aging report, and evaluate credit terms.";
        if (IsPayablesException(ex))
            return "Verify vendor balances, inspect statement of accounts, and review disputed invoices.";
        if (IsRelatedPartyException(ex))
            return "Examine board approvals, arm's length pricing, and AS-18/Sec 188 disclosures.";
        if (IsPeriodEndException(ex))
            return "Verify year-end cutoff procedures, accrual reversals, and post-closing entries.";
        if (IsMasterDataException(ex))
            return "Update missing PAN/GSTIN in Tally ledgers, verify party pin codes, and standardize master records.";

        return "Review transaction documentation, authorization sign-off, and ledger classification.";
    }

    // Classifiers
    private static bool IsGstException(AuditException e) =>
        e.Category == RuleCategory.GST ||
        e.RuleId.StartsWith("GST", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("GST", StringComparison.OrdinalIgnoreCase);

    private static bool IsTdsException(AuditException e) =>
        e.Category == RuleCategory.TDS ||
        e.RuleId.StartsWith("TDS", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("TDS", StringComparison.OrdinalIgnoreCase);

    private static bool IsDuplicateException(AuditException e) =>
        e.Category == RuleCategory.DuplicateDetection ||
        e.RuleId.StartsWith("DUP", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Duplicate", StringComparison.OrdinalIgnoreCase);

    private static bool IsJournalException(AuditException e) =>
        e.RuleId.Contains("JRN", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Journal", StringComparison.OrdinalIgnoreCase) ||
        (e.EvidenceJson != null && e.EvidenceJson.Contains("Journal", StringComparison.OrdinalIgnoreCase));

    private static bool IsSalesException(AuditException e) =>
        e.RuleName.Contains("Sales", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Output", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Debtor", StringComparison.OrdinalIgnoreCase) ||
        (e.EvidenceJson != null && e.EvidenceJson.Contains("Sales", StringComparison.OrdinalIgnoreCase));

    private static bool IsPurchaseException(AuditException e) =>
        e.RuleName.Contains("Purchase", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Input Tax", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Creditor", StringComparison.OrdinalIgnoreCase) ||
        (e.EvidenceJson != null && e.EvidenceJson.Contains("Purchase", StringComparison.OrdinalIgnoreCase));

    private static bool IsExpenseException(AuditException e) =>
        e.RuleName.Contains("Expense", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Payment", StringComparison.OrdinalIgnoreCase) ||
        e.RuleId.Contains("CSH-01", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Cash Payment", StringComparison.OrdinalIgnoreCase);

    private static bool IsBankException(AuditException e) =>
        e.Category == RuleCategory.Banking ||
        e.RuleId.StartsWith("BNK", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Bank", StringComparison.OrdinalIgnoreCase) ||
        (e.LedgerName != null && e.LedgerName.Contains("Bank", StringComparison.OrdinalIgnoreCase));

    private static bool IsCashException(AuditException e) =>
        e.RuleId.Contains("CSH", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Cash", StringComparison.OrdinalIgnoreCase) ||
        (e.LedgerName != null && e.LedgerName.Contains("Cash", StringComparison.OrdinalIgnoreCase));

    private static bool IsLedgerException(AuditException e) =>
        e.RuleName.Contains("Ledger", StringComparison.OrdinalIgnoreCase) ||
        e.RuleId.Contains("LED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(e.EntityType, "Ledger", StringComparison.OrdinalIgnoreCase);

    private static bool IsReceivablesException(AuditException e) =>
        e.RuleName.Contains("Receivable", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Debtor", StringComparison.OrdinalIgnoreCase) ||
        (e.SuggestedCorrection != null && e.SuggestedCorrection.Contains("Receivable", StringComparison.OrdinalIgnoreCase));

    private static bool IsPayablesException(AuditException e) =>
        e.RuleName.Contains("Payable", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Creditor", StringComparison.OrdinalIgnoreCase) ||
        (e.SuggestedCorrection != null && e.SuggestedCorrection.Contains("Payable", StringComparison.OrdinalIgnoreCase));

    private static bool IsRelatedPartyException(AuditException e) =>
        e.RuleName.Contains("Related Party", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Director", StringComparison.OrdinalIgnoreCase) ||
        e.RuleId.Contains("REL", StringComparison.OrdinalIgnoreCase);

    private static bool IsPeriodEndException(AuditException e) =>
        e.RuleId.Contains("PRD", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Period End", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Holiday", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Weekend", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Cutoff", StringComparison.OrdinalIgnoreCase);

    private static bool IsMasterDataException(AuditException e) =>
        e.RuleId.Contains("MST", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Master Data", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Missing GSTIN", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("PAN Not", StringComparison.OrdinalIgnoreCase);

    private static bool IsReconciliationException(AuditException e) =>
        e.RuleId.StartsWith("REC", StringComparison.OrdinalIgnoreCase) ||
        e.RuleName.Contains("Reconciliation", StringComparison.OrdinalIgnoreCase);
}
