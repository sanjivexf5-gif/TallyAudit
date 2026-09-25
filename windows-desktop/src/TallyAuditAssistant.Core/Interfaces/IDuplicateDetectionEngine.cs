using TallyAuditAssistant.Core.Domain.Duplicates;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IDuplicateDetectionEngine
{
    DuplicateStrategyConfiguration Configuration { get; set; }

    Task<DuplicateAuditSummary> DetectDuplicatesAsync(
        string companyId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        DuplicateStrategyConfiguration? overrideConfig = null,
        CancellationToken cancellationToken = default);

    event EventHandler<DuplicateAuditProgress>? ProgressChanged;
}

public interface IDuplicateRepository
{
    Task SaveMatchPairsBatchAsync(IEnumerable<DuplicateMatchPair> matchPairs, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DuplicateMatchPair>> GetMatchPairsAsync(
        string companyId,
        DuplicateConfidenceTier? tier = null,
        string? voucherType = null,
        CancellationToken cancellationToken = default);

    Task<DuplicateAuditSummary> GetSummaryAsync(string companyId, CancellationToken cancellationToken = default);

    Task UpdateReviewStatusAsync(string matchId, Core.Domain.Audit.ReviewStatus status, string reviewer, string? notes, CancellationToken cancellationToken = default);
}
