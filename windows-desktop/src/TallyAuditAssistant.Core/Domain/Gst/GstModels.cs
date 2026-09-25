using System.Text.Json;
using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Domain.Gst;

public enum GstCheckStatus
{
    Passed = 0,
    Exception = 1,
    UnableToDetermine = 2
}

public class GstRuleDefinition
{
    public string RuleId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime EffectiveDate { get; set; } = new DateTime(2017, 7, 1);
    public DateTime? ExpiryDate { get; set; }
    public string Jurisdiction { get; set; } = "IN-ALL";
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

public class GstCheckResult
{
    public string ResultId { get; set; } = Guid.NewGuid().ToString();
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string? VoucherId { get; set; }
    public string? VoucherNumber { get; set; }
    public DateTime? VoucherDate { get; set; }
    public string? VoucherTypeName { get; set; }
    public string? PartyLedgerName { get; set; }
    public string? PartyGstin { get; set; }
    public decimal? TaxableAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? CgstAmount { get; set; }
    public decimal? SgstAmount { get; set; }
    public decimal? IgstAmount { get; set; }
    public string? PlaceOfSupply { get; set; }
    public SeverityLevel Severity { get; set; } = SeverityLevel.Medium;
    public GstCheckStatus Status { get; set; } = GstCheckStatus.Exception;
    public string Explanation { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "{}";
    public string Jurisdiction { get; set; } = "IN-ALL";
    public string SourceReference { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pending;
    public string? Reviewer { get; set; }
    public string? ReviewerNote { get; set; }
}

public record GstAuditContext(
    string CompanyId,
    DateTime FromDate,
    DateTime ToDate,
    string? CompanyGstin = null,
    string? CompanyStateCode = null
);

public record GstAuditProgress(
    string CurrentRuleId,
    string CurrentRuleName,
    int RulesExecuted,
    int TotalRules,
    int TransactionsProcessed,
    int ExceptionsFound,
    int UnableToDetermineCount,
    double Percentage
);

public class GstAuditSummary
{
    public int TotalTransactionsChecked { get; set; }
    public int PassedCount { get; set; }
    public int ExceptionCount { get; set; }
    public int HighSeverityCount { get; set; }
    public int MediumSeverityCount { get; set; }
    public int LowSeverityCount { get; set; }
    public int UnableToDetermineCount { get; set; }
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
}

public class GstVoucherDetail
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
    public string? PartyGstin { get; set; }
    public string? PartyState { get; set; }
    public string? PlaceOfSupply { get; set; }
    public bool IsReverseCharge { get; set; }
    public List<GstVoucherEntryDetail> Entries { get; set; } = new();
}

public class GstVoucherEntryDetail
{
    public string EntryId { get; set; } = string.Empty;
    public string LedgerName { get; set; } = string.Empty;
    public string? ParentGroup { get; set; }
    public decimal Amount { get; set; }
    public bool IsDebit { get; set; }
    public string? TaxType { get; set; }
    public decimal? GstRate { get; set; }
    public string? HsnCode { get; set; }
}
