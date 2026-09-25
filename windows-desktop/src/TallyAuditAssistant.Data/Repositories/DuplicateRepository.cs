using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Duplicates;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data.Repositories;

public class DuplicateRepository : IDuplicateRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<DuplicateRepository> _logger;

    public DuplicateRepository(SqliteConnectionFactory connectionFactory, ILogger<DuplicateRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task SaveMatchPairsBatchAsync(IEnumerable<DuplicateMatchPair> matchPairs, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        const string sql = @"
            INSERT INTO Exceptions (
                Id, CompanyId, RuleId, RuleName, Category, Severity, VoucherId, LedgerId,
                VoucherNumber, VoucherDate, FlaggedAmount, Explanation, EvidenceJson, Status
            )
            VALUES (
                @MatchId, @CompanyId, @RuleId, @RuleName, @Category, @Severity, @VoucherId, @LedgerId,
                @VoucherNumber, @VoucherDate, @FlaggedAmount, @Explanation, @EvidenceJson, @Status
            )
            ON CONFLICT(Id) DO UPDATE SET
                Explanation = excluded.Explanation,
                EvidenceJson = excluded.EvidenceJson,
                Status = excluded.Status;
        ";

        foreach (var pair in matchPairs)
        {
            string severity = pair.Tier switch
            {
                DuplicateConfidenceTier.ExactDuplicate => "High",
                DuplicateConfidenceTier.LikelyDuplicate => "Medium",
                _ => "Low"
            };

            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                pair.MatchId,
                pair.CompanyId,
                RuleId = $"DUP-{pair.Tier}",
                RuleName = $"{pair.TierLabel} ({pair.ConfidenceScore:F0}%)",
                Category = $"Duplicate - {pair.VoucherCategory}",
                Severity = severity,
                VoucherId = pair.PotentialDuplicate.VoucherId,
                LedgerId = pair.OriginalTransaction.PartyLedgerName,
                pair.PotentialDuplicate.VoucherNumber,
                VoucherDate = pair.PotentialDuplicate.VoucherDate.ToString("yyyy-MM-dd"),
                FlaggedAmount = Math.Abs(pair.PotentialDuplicate.TotalAmount),
                pair.Explanation,
                pair.EvidenceJson,
                Status = pair.ReviewStatus.ToString()
            }, tx, cancellationToken: cancellationToken));
        }

        tx.Commit();
        _logger.LogInformation("Saved {Count} duplicate match pairs into SQLite database", matchPairs.Count());
    }

    public async Task<IReadOnlyList<DuplicateMatchPair>> GetMatchPairsAsync(
        string companyId,
        DuplicateConfidenceTier? tier = null,
        string? voucherType = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT Id as MatchId, CompanyId, RuleId, RuleName, Category, Severity, VoucherNumber, VoucherDate,
                   FlaggedAmount, Explanation, EvidenceJson, Status as ReviewStatus
            FROM Exceptions
            WHERE CompanyId = @CompanyId AND Category LIKE 'Duplicate%'
            ORDER BY DetectedAt DESC;
        ";

        var rows = await connection.QueryAsync(new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: cancellationToken));
        var list = new List<DuplicateMatchPair>();

        foreach (var r in rows)
        {
            var pair = new DuplicateMatchPair
            {
                MatchId = (string)r.MatchId,
                CompanyId = (string)r.CompanyId,
                StrategyUsed = (string)r.RuleName,
                Explanation = (string)r.Explanation,
                EvidenceJson = (string)(r.EvidenceJson ?? "{}"),
                ReviewStatus = Enum.TryParse<ReviewStatus>((string)r.ReviewStatus, out var s) ? s : ReviewStatus.Pending
            };
            list.Add(pair);
        }

        return list;
    }

    public async Task<DuplicateAuditSummary> GetSummaryAsync(string companyId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT 
                COUNT(*) as TotalPairs,
                SUM(CASE WHEN Severity = 'High' THEN 1 ELSE 0 END) as ExactCount,
                SUM(CASE WHEN Severity = 'Medium' THEN 1 ELSE 0 END) as LikelyCount,
                SUM(CASE WHEN Severity = 'Low' THEN 1 ELSE 0 END) as PossibleCount,
                SUM(FlaggedAmount) as TotalExposure
            FROM Exceptions
            WHERE CompanyId = @CompanyId AND Category LIKE 'Duplicate%';
        ";

        var row = await connection.QuerySingleOrDefaultAsync(new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: cancellationToken));

        return new DuplicateAuditSummary
        {
            TotalDuplicatePairsFound = row != null && row.TotalPairs != null && !(row.TotalPairs is DBNull) ? Convert.ToInt32(row.TotalPairs) : 0,
            ExactDuplicatesCount = row != null && row.ExactCount != null && !(row.ExactCount is DBNull) ? Convert.ToInt32(row.ExactCount) : 0,
            LikelyDuplicatesCount = row != null && row.LikelyCount != null && !(row.LikelyCount is DBNull) ? Convert.ToInt32(row.LikelyCount) : 0,
            PossibleDuplicatesCount = row != null && row.PossibleCount != null && !(row.PossibleCount is DBNull) ? Convert.ToInt32(row.PossibleCount) : 0,
            TotalPotentialExposureRupees = row != null && row.TotalExposure != null && !(row.TotalExposure is DBNull) ? Convert.ToDecimal(row.TotalExposure) : 0m,
            EvaluatedAt = DateTime.UtcNow
        };
    }

    public async Task UpdateReviewStatusAsync(string matchId, ReviewStatus status, string reviewer, string? notes, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            UPDATE Exceptions
            SET Status = @Status, Reviewer = @Reviewer, ReviewerNote = @Notes, ReviewedAt = CURRENT_TIMESTAMP
            WHERE Id = @MatchId;
        ";

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            MatchId = matchId,
            Status = status.ToString(),
            Reviewer = reviewer,
            Notes = notes
        }, cancellationToken: cancellationToken));
    }
}
