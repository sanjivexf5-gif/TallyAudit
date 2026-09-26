using System;
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
}
