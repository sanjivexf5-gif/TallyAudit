using System.Text.Json;
using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Domain.Tds;

public enum TdsCheckStatus
{
    Passed = 0,
    Exception = 1,
    ReviewRequiredInsufficientData = 2
}

public class TdsRuleDefinition
{
    public string RuleId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty; // e.g. "194C", "194J", "194I", "194H", "194Q", "206AA"
    public string Description { get; set; } = string.Empty;
    public DateTime EffectiveDate { get; set; } = new DateTime(2020, 4, 1);
    public DateTime? ExpiryDate { get; set; }
    public string Jurisdiction { get; set; } = "IN-IT-ACT";
    public string Version { get; set; } = "1.0.0";
    public string SourceReference { get; set; } = string.Empty;
    public SeverityLevel Severity { get; set; } = SeverityLevel.Medium;
    public bool Enabled { get; set; } = true;
    public Dictionary<string, object> Parameters { get; set; } = new();

    public string ParametersJson
    {
        get => JsonSerializer.Serialize(Parameters);
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                try { Parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(value) ?? new(); }
                catch { Parameters = new(); }
            }
        }
    }
}

public class TdsCheckResult
{
    public string ResultId { get; set; } = Guid.NewGuid().ToString();
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string? VoucherId { get; set; }
    public string? VoucherNumber { get; set; }
    public DateTime? VoucherDate { get; set; }
    public string? VoucherTypeName { get; set; }
    public string? PartyLedgerId { get; set; }
    public string? PartyLedgerName { get; set; }
    public string? PartyPan { get; set; }
    public string? ExpenseLedgerName { get; set; }
    public decimal? TransactionAmount { get; set; }
    public decimal? CumulativeVendorAmount { get; set; }
    public decimal? SectionThreshold { get; set; }
    public decimal? DeductedTdsAmount { get; set; }
    public decimal? ExpectedTdsAmount { get; set; }
    public decimal? AppliedRate { get; set; }
    public decimal? ExpectedRate { get; set; }
    public SeverityLevel Severity { get; set; } = SeverityLevel.Medium;
    public TdsCheckStatus Status { get; set; } = TdsCheckStatus.Exception;
    public string Explanation { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "{}";
    public string Jurisdiction { get; set; } = "IN-IT-ACT";
    public string SourceReference { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pending;
    public string? Reviewer { get; set; }
    public string? ReviewerNote { get; set; }
}

public record TdsAuditContext(
    string CompanyId,
    DateTime FromDate,
    DateTime ToDate,
    string? CompanyPan = null,
    string? CompanyTan = null
);

public record TdsAuditProgress(
    string CurrentRuleId,
    string CurrentRuleName,
    int RulesExecuted,
    int TotalRules,
    int TransactionsProcessed,
    int ExceptionsFound,
    int InsufficientDataCount,
    double Percentage
);

public class TdsAuditSummary
{
    public int TotalTransactionsChecked { get; set; }
    public int PassedCount { get; set; }
    public int ExceptionCount { get; set; }
    public int HighSeverityCount { get; set; }
    public int MediumSeverityCount { get; set; }
    public int LowSeverityCount { get; set; }
    public int ReviewRequiredInsufficientDataCount { get; set; }
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
}

public class TdsVoucherDetail
{
    public string VoucherId { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string VoucherTypeName { get; set; } = string.Empty;
    public string? VoucherNumber { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime VoucherDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Narration { get; set; }
    public string? PartyLedgerName { get; set; }
    public string? PartyPan { get; set; }
    public string? PartyCategory { get; set; }
    public List<TdsVoucherEntryDetail> Entries { get; set; } = new();
}

public class TdsVoucherEntryDetail
{
    public string Id { get; set; } = string.Empty;
    public string LedgerName { get; set; } = string.Empty;
    public string? ParentGroup { get; set; }
    public decimal Amount { get; set; }
    public bool IsDebit { get; set; }
    public string? TdsSection { get; set; }
    public decimal? TdsRate { get; set; }
    public string? TaxType { get; set; }
}
