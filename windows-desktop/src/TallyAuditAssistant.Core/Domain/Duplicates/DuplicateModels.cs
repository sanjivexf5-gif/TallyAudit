using System.Text.Json;
using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Domain.Duplicates;

public enum DuplicateConfidenceTier
{
    ExactDuplicate = 1,    // 100% confidence: Exact Date + Party + Amount + Voucher/Ref Number
    LikelyDuplicate = 2,   // 80-99% confidence: Same Party + Exact Amount + Similar Date (±N days) + Same Type
    PossibleDuplicate = 3  // 50-79% confidence: Same Party + Similar Amount + Similar Date + Similar Narration
}

public enum DuplicateVoucherCategory
{
    All = 0,
    SalesInvoice = 1,
    PurchaseInvoice = 2,
    Receipt = 3,
    Payment = 4,
    Journal = 5,
    CreditNote = 6,
    DebitNote = 7
}

public class DuplicateStrategyConfiguration
{
    public bool EnableExactMatch { get; set; } = true;
    public bool EnableStrongMatch { get; set; } = true;
    public bool EnablePossibleMatch { get; set; } = true;

    // Date proximity window
    public int StrongMatchDateWindowDays { get; set; } = 3;
    public int PossibleMatchDateWindowDays { get; set; } = 15;

    // Amount tolerance
    public decimal PossibleMatchAmountTolerancePercent { get; set; } = 1.0m;
    public decimal PossibleMatchAmountToleranceRupees { get; set; } = 10.0m;

    // Narration text similarity threshold (0.0 to 1.0)
    public double NarrationSimilarityThreshold { get; set; } = 0.65;

    // Target Voucher Types
    public HashSet<string> IncludedVoucherTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sales", "Purchase", "Receipt", "Payment", "Journal", "Credit Note", "Debit Note"
    };
}

public class TransactionSnapshot
{
    public string VoucherId { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public DateTime VoucherDate { get; set; }
    public string VoucherTypeName { get; set; } = string.Empty;
    public string PartyLedgerName { get; set; } = string.Empty;
    public string? PartyGstin { get; set; }
    public string? PartyPan { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Narration { get; set; }
    public int LineItemCount { get; set; }
}

public class FieldComparisonResult
{
    public string FieldName { get; set; } = string.Empty;
    public string OriginalValue { get; set; } = string.Empty;
    public string DuplicateValue { get; set; } = string.Empty;
    public bool IsMatched { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class DuplicateMatchPair
{
    public string MatchId { get; set; } = Guid.NewGuid().ToString();
    public string CompanyId { get; set; } = string.Empty;
    public DuplicateConfidenceTier Tier { get; set; }
    public string TierLabel => Tier switch
    {
        DuplicateConfidenceTier.ExactDuplicate => "Exact Duplicate",
        DuplicateConfidenceTier.LikelyDuplicate => "Likely Duplicate",
        DuplicateConfidenceTier.PossibleDuplicate => "Possible Duplicate",
        _ => "Possible Duplicate"
    };

    public double ConfidenceScore { get; set; } // e.g. 100.0, 92.5, 68.0
    public string StrategyUsed { get; set; } = string.Empty;
    public string VoucherCategory { get; set; } = string.Empty;

    // The two compared transactions
    public TransactionSnapshot OriginalTransaction { get; set; } = new();
    public TransactionSnapshot PotentialDuplicate { get; set; } = new();

    // Field-level comparison breakdown
    public List<string> MatchingFields { get; set; } = new();
    public List<string> DifferenceFields { get; set; } = new();
    public List<FieldComparisonResult> DetailedFieldComparisons { get; set; } = new();

    // Explanation & Evidence
    public string Explanation { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "{}";

    // Auditor Workflow
    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pending;
    public string? Reviewer { get; set; }
    public string? ReviewerNote { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

public record DuplicateAuditProgress(
    string CurrentPhase,
    int BatchesProcessed,
    int TotalBatches,
    int ExactMatchesFound,
    int LikelyMatchesFound,
    int PossibleMatchesFound,
    double Percentage
);

public class DuplicateAuditSummary
{
    public int TotalVouchersScanned { get; set; }
    public int TotalDuplicatePairsFound { get; set; }
    public int ExactDuplicatesCount { get; set; }
    public int LikelyDuplicatesCount { get; set; }
    public int PossibleDuplicatesCount { get; set; }

    // Breakdown by Voucher Type
    public int SalesDuplicatesCount { get; set; }
    public int PurchaseDuplicatesCount { get; set; }
    public int ReceiptDuplicatesCount { get; set; }
    public int PaymentDuplicatesCount { get; set; }
    public int JournalDuplicatesCount { get; set; }
    public int CreditDebitNoteDuplicatesCount { get; set; }

    public decimal TotalPotentialExposureRupees { get; set; }
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
}
