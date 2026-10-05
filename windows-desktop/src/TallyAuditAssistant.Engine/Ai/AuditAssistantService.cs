using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Engine.Ai;

public class AuditAssistantService : IAuditAssistantService
{
    private readonly IAuditAiProvider _aiProvider;

    public bool IsAiEnabled => _aiProvider.IsConfigured;
    public string CurrentProviderName => _aiProvider.ProviderName;

    public AuditAssistantService(IAuditAiProvider aiProvider)
    {
        _aiProvider = aiProvider;
    }

    public async Task<string> ExplainFindingAsync(AuditException exception, CancellationToken cancellationToken = default)
    {
        const string systemPrompt = @"You are an expert, professional audit assistant. Your job is to explain a specific audit rule finding clearly and neutrally.
You must adhere to the following rules:
1. Do NOT independently determine whether a transaction is fraudulent, illegal, or tax-evasive. 
2. Use neutral audit-analysis terminology, such as 'potential exception', 'review required', or 'rule triggered'.
3. Follow the STRICT RESPONSE FORMAT, separating:
   - FACT: What are the concrete recorded values?
   - RULE RESULT: What rule triggered and why?
   - POSSIBLE INTERPRETATION: Explain that these are possibilities, not absolute truths (e.g., could be a formatting error, rounding difference, or missing voucher link).
   - REVIEW QUESTION: Suggest what the human auditor should check.";

        var userPrompt = $@"
Company Name: [Sanitized]
Rule Code: {exception.RuleId}
Rule Name: {exception.RuleName}
Category: {exception.Category}
Severity: {exception.Severity}
Voucher Number: {exception.VoucherNumber ?? "—"}
Voucher Date: {exception.VoucherDate?.ToString("dd-MMM-yyyy") ?? "—"}
Ledger/Party: {exception.LedgerName ?? "—"}
Flagged Amount: {exception.FlaggedAmount?.ToString("C") ?? "—"}
Evidence: {exception.EvidenceJson}
Rule Trigger Explanation: {exception.SuggestedCorrection ?? "—"}";

        return await _aiProvider.GenerateTextAsync(systemPrompt, userPrompt, cancellationToken);
    }

    public async Task<string> SuggestReviewQuestionsAsync(AuditException exception, CancellationToken cancellationToken = default)
    {
        const string systemPrompt = @"You are a professional auditor copilot. Suggest 4-5 constructive, neutral, and precise review questions for the human auditor to investigate regarding the triggered rule. 
Do not imply any guilt, tax evasion, or fraud. Keep questions strictly professional, focusing on voucher classification, supporting documentation, party mapping, and ledger consistency.";

        var userPrompt = $@"
Rule Code: {exception.RuleId}
Rule Name: {exception.RuleName}
Category: {exception.Category}
Ledger: {exception.LedgerName ?? "—"}
Evidence: {exception.EvidenceJson}
Trigger Reason: {exception.SuggestedCorrection ?? "—"}";

        return await _aiProvider.GenerateTextAsync(systemPrompt, userPrompt, cancellationToken);
    }

    public async Task<string> DraftWorkingPaperRemarkAsync(AuditException exception, CancellationToken cancellationToken = default)
    {
        const string systemPrompt = @"You are an assistant to a corporate auditor. Draft a concise, highly professional working paper note or remark for the selected exception.
Your draft must follow this structure:
Observation: [Descriptive summary of what was detected in neutral audit-analysis terminology, e.g. 'Difference identified during reconciliation']
Verification: [What supporting documentation or clarification the auditor should verify]
Conclusion: [State that the final conclusion is left for the auditor to determine after completing verification]
 
Strictly avoid conclusions such as 'Fraud detected' or 'Tax evasion'. Only use phrases like 'Transaction requires verification' or 'Supporting documentation should be reviewed'.";

        var userPrompt = $@"
Rule Name: {exception.RuleName}
Category: {exception.Category}
Voucher Number: {exception.VoucherNumber ?? "—"}
Amount: {exception.FlaggedAmount?.ToString("C") ?? "—"}
Details: {exception.SuggestedCorrection ?? "—"}";

        return await _aiProvider.GenerateTextAsync(systemPrompt, userPrompt, cancellationToken);
    }

    public async Task<string> ExplainReconciliationAsync(AuditException exception, CancellationToken cancellationToken = default)
    {
        const string systemPrompt = @"You are an expert financial reconciler assistant. Explain the reconciliation mismatch or difference in a professional and constructive manner.
Explain:
1. What datasets were compared (e.g. Sales Ledger vs GST Tax Ledger).
2. What difference was computed (Expected vs Actual).
3. Neutral potential causes (e.g. timing differences, ledger mapping, rounding off, or incomplete synchronization).
4. Concrete steps for the auditor to verify the records.

Your explanation must state possibilities, not facts. Do not make any compliance conclusions.";

        var userPrompt = $@"
Reconciliation Rule: {exception.RuleName}
Rule Id: {exception.RuleId}
Voucher Number: {exception.VoucherNumber ?? "—"}
Ledger Name: {exception.LedgerName ?? "—"}
Mismatch Difference Amount: {exception.FlaggedAmount?.ToString("C") ?? "—"}
Evidence: {exception.EvidenceJson}
Explanation: {exception.SuggestedCorrection ?? "—"}";

        return await _aiProvider.GenerateTextAsync(systemPrompt, userPrompt, cancellationToken);
    }

    public async Task<string> SummarizeAuditRunAsync(
        int transactionsAudited, 
        int rulesExecuted, 
        int findings, 
        int highPriority, 
        int reviewRequired, 
        string additionalStatsJson, 
        CancellationToken cancellationToken = default)
    {
        const string systemPrompt = @"You are an executive audit reporter. Summarize the completed automated audit run based on the provided aggregate statistics.
Provide a professional, high-level summary that contains:
1. Overview of the audit execution scope.
2. Highlight key focus categories requiring review (e.g., GST or TDS) based on the findings counts.
3. Highlight general patterns (e.g., recurring reconciliation differences or missing narrations) indicated by the counts.
4. Suggested review priorities for the auditor.

Rules:
- Do NOT draw legal, political, or business compliance conclusions.
- State findings clearly and neutrally.
- The summary must be generated solely from the provided statistics. Do not invent any names or transactions.";

        var userPrompt = $@"
Transactions Audited: {transactionsAudited}
Rules Executed: {rulesExecuted}
Total Findings Discovered: {findings}
High Severity Findings: {highPriority}
Review Status Pending: {reviewRequired}
Detailed Exception Counts by Category: {additionalStatsJson}";

        return await _aiProvider.GenerateTextAsync(systemPrompt, userPrompt, cancellationToken);
    }

    public async Task<string> GenerateRiskSummaryAsync(AuditDashboardSummary summary, CancellationToken cancellationToken = default)
    {
        if (summary == null)
        {
            return "No audit dashboard summary available to summarize.";
        }

        const string systemPrompt = @"You are an executive audit risk analyst. Generate a clear, professional, and objective risk summary based strictly on the provided audit dashboard summary metrics.
You must adhere to the following rules:
1. Do NOT determine fraud, guilt, tax evasion, or legal conclusions. Use neutral audit terminology (e.g., 'elevated risk score', 'audit exceptions identified', 'review recommended').
2. Rely exclusively on the provided deterministic numbers, risk scores, and area summaries. Do not invent or extrapolate any transactions, amounts, or entities.
3. Structure your response clearly:
   - EXECUTIVE OVERVIEW: Overall risk level, score, and total exceptions found.
   - KEY RISK DRIVERS: Specific audit areas contributing most significantly to the risk score.
   - MONETARY EXPOSURE: Summary of exception amounts identified across areas.
   - AUDITOR RECOMMENDATION: Next steps for the audit team in reviewing pending items.";

        var areaBreakdown = summary.AuditAreaSummaries != null && summary.AuditAreaSummaries.Count > 0
            ? string.Join("\n", summary.AuditAreaSummaries.Select(a => $"- {a.AreaName}: {a.FindingCount} findings (Critical: {a.CriticalCount}, High: {a.HighCount}, Med: {a.MediumCount}, Low: {a.LowCount}) | Exception Amount: {a.ExceptionAmount:C} | Risk Score: {a.RiskScore}"))
            : "No audit area breakdown available.";

        var topFindingsList = summary.TopFindings != null && summary.TopFindings.Count > 0
            ? string.Join("\n", summary.TopFindings.Take(5).Select(f => $"- [{f.Severity}] {f.Title} ({f.Category}): {f.Amount:C} - {f.Description}"))
            : "No top findings recorded.";

        var userPrompt = $@"
Company: {(string.IsNullOrWhiteSpace(summary.CompanyName) ? "Active Company" : summary.CompanyName)}
Financial Year: {(string.IsNullOrWhiteSpace(summary.FinancialYear) ? "Current Period" : summary.FinancialYear)}
Overall Risk Score: {summary.OverallRiskScore} / 100 ({summary.OverallRiskLevel} Risk)
Total Findings: {summary.TotalFindings} (Critical: {summary.CriticalCount}, High: {summary.HighCount}, Medium: {summary.MediumCount}, Low: {summary.LowCount}, Informational: {summary.InformationalCount})
Finding Status: {summary.OpenFindings} Open, {summary.ReviewedFindings} Reviewed, {summary.ResolvedFindings} Resolved
Total Exception Amount: {summary.TotalExceptionAmount:C}
- GST Exception Amount: {summary.GstExceptionAmount:C}
- TDS Exception Amount: {summary.TdsExceptionAmount:C}
- Duplicate Exception Amount: {summary.DuplicateExceptionAmount:C}
- Journal Exception Amount: {summary.JournalExceptionAmount:C}
- Sales Exception Amount: {summary.SalesExceptionAmount:C}
- Purchase Exception Amount: {summary.PurchaseExceptionAmount:C}
- Expense Exception Amount: {summary.ExpenseExceptionAmount:C}

Audit Areas:
{areaBreakdown}

Top Priority Findings:
{topFindingsList}";

        return await _aiProvider.GenerateTextAsync(systemPrompt, userPrompt, cancellationToken);
    }

    public async Task<string> SuggestInvestigationFocusAsync(AuditDashboardSummary summary, CancellationToken cancellationToken = default)
    {
        if (summary == null)
        {
            return "No audit dashboard summary available to suggest investigation focus.";
        }

        const string systemPrompt = @"You are a senior audit engagement manager. Based strictly on the provided audit dashboard summary and top findings, propose actionable, prioritized investigation focus areas and specific verification procedures for the audit team.
You must adhere to the following rules:
1. Do NOT imply guilt, illegality, or intentional wrongdoing. Use neutral, standard auditing guidance.
2. Ground all suggestions only in the provided audit areas, finding titles, and exception categories.
3. Structure your response with:
   - PRIORITY INVESTIGATION AREAS: The top 2-3 audit categories requiring immediate auditor attention.
   - TARGETED AUDIT PROCEDURES: Concrete verification steps (e.g., matching e-way bills with GSTR-2B, obtaining third-party confirmations, inspecting supporting vouchers, checking ledger classifications).
   - SAMPLE SELECTION GUIDANCE: How to prioritize sample testing among the open findings.";

        var areaBreakdown = summary.AuditAreaSummaries != null && summary.AuditAreaSummaries.Count > 0
            ? string.Join("\n", summary.AuditAreaSummaries.Where(a => a.FindingCount > 0).OrderByDescending(a => a.RiskScore).Select(a => $"- {a.AreaName}: {a.FindingCount} findings (Risk Score: {a.RiskScore}, Total Amount: {a.ExceptionAmount:C})"))
            : "No audit area findings recorded.";

        var topFindingsList = summary.TopFindings != null && summary.TopFindings.Count > 0
            ? string.Join("\n", summary.TopFindings.Take(8).Select(f => $"- [{f.Severity}] {f.Title} ({f.Category}): Amount: {f.Amount:C} | Procedure: {f.RecommendedAuditProcedure} | Source: {f.SourceReference}"))
            : "No specific top findings recorded.";

        var userPrompt = $@"
Company: {(string.IsNullOrWhiteSpace(summary.CompanyName) ? "Active Company" : summary.CompanyName)}
Financial Period: {(string.IsNullOrWhiteSpace(summary.FinancialYear) ? "Current Period" : summary.FinancialYear)}
Overall Risk Level: {summary.OverallRiskLevel} (Score: {summary.OverallRiskScore}/100)
Total Open Findings: {summary.OpenFindings} (Critical: {summary.CriticalCount}, High: {summary.HighCount})
Total Exception Amount: {summary.TotalExceptionAmount:C}

Active Audit Area Findings:
{areaBreakdown}

Top Discovered Findings:
{topFindingsList}";

        return await _aiProvider.GenerateTextAsync(systemPrompt, userPrompt, cancellationToken);
    }
}

