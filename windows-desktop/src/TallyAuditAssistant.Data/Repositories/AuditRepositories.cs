using Dapper;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data.Repositories;

public class AuditRuleRepository : IAuditRuleRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<AuditRuleRepository> _logger;

    public AuditRuleRepository(SqliteConnectionFactory connectionFactory, ILogger<AuditRuleRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AuditRule>> GetAllRulesAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM AuditRules ORDER BY Category, RuleId;";
        var results = await connection.QueryAsync<AuditRule>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return results.ToList();
    }

    public async Task<AuditRule?> GetRuleByIdAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT * FROM AuditRules WHERE RuleId = @RuleId LIMIT 1;";
        return await connection.QuerySingleOrDefaultAsync<AuditRule>(new CommandDefinition(sql, new { RuleId = ruleId }, cancellationToken: cancellationToken));
    }

    public async Task SaveRuleAsync(AuditRule rule, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            INSERT INTO AuditRules (RuleId, Category, Name, Description, Severity, SuggestedReview, ParametersJson, IsEnabled, Version, EffectiveFrom, EffectiveTo)
            VALUES (@RuleId, @Category, @Name, @Description, @Severity, @SuggestedReview, @ParametersJson, @IsEnabled, @Version, @EffectiveFrom, @EffectiveTo)
            ON CONFLICT(RuleId) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                Severity = excluded.Severity,
                SuggestedReview = excluded.SuggestedReview,
                ParametersJson = excluded.ParametersJson,
                IsEnabled = excluded.IsEnabled,
                Version = excluded.Version,
                EffectiveFrom = excluded.EffectiveFrom,
                EffectiveTo = excluded.EffectiveTo;
        ";
        await connection.ExecuteAsync(new CommandDefinition(sql, rule, cancellationToken: cancellationToken));
    }

    public async Task SetRuleEnabledAsync(string ruleId, bool isEnabled, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "UPDATE AuditRules SET IsEnabled = @IsEnabled WHERE RuleId = @RuleId;";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { RuleId = ruleId, IsEnabled = isEnabled ? 1 : 0 }, cancellationToken: cancellationToken));
    }

    public async Task UpdateRuleParametersAsync(string ruleId, string parametersJson, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "UPDATE AuditRules SET ParametersJson = @ParametersJson WHERE RuleId = @RuleId;";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { RuleId = ruleId, ParametersJson = parametersJson }, cancellationToken: cancellationToken));
    }
}

public class AuditResultRepository : IAuditResultRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IAuditTrailService? _auditTrailService;
    private readonly ILogger<AuditResultRepository> _logger;

    public AuditResultRepository(
        SqliteConnectionFactory connectionFactory, 
        ILogger<AuditResultRepository> logger,
        IAuditTrailService? auditTrailService = null)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _auditTrailService = auditTrailService;
    }

    public async Task SaveResultsBatchAsync(IReadOnlyList<AuditResult> results, CancellationToken cancellationToken = default)
    {
        if (results.Count == 0) return;

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

        // 1. Ensure all RuleIds exist in AuditRules catalog
        const string ensureRuleSql = @"
            INSERT OR IGNORE INTO AuditRules (RuleId, Category, Name, Description, Severity, SuggestedReview, Version, IsEnabled)
            VALUES (@RuleId, @Category, @RuleName, @Explanation, @Severity, 'Review transaction details and supporting audit evidence.', '1.0.0', 1);
        ";

        var uniqueRules = results.Select(r => new
        {
            r.RuleId,
            Category = (int)r.Category,
            r.RuleName,
            Explanation = !string.IsNullOrEmpty(r.Explanation) ? r.Explanation : r.RuleName,
            Severity = (int)r.Severity
        }).GroupBy(x => x.RuleId).Select(g => g.First());

        await connection.ExecuteAsync(new CommandDefinition(ensureRuleSql, uniqueRules, tx, cancellationToken: cancellationToken));

        // 2. Ensure all CompanyIds exist in Companies table
        const string ensureCompanySql = @"
            INSERT OR IGNORE INTO Companies (Id, TallyCompanyName, FormalName, BooksFromDate, LastSyncDate, IsActive, CreatedAt)
            VALUES (@CompanyId, @CompanyId, @CompanyId, '2025-04-01', CURRENT_TIMESTAMP, 1, CURRENT_TIMESTAMP);
        ";

        var uniqueCompanies = results.Select(r => new { r.CompanyId }).GroupBy(x => x.CompanyId).Select(g => g.First());
        await connection.ExecuteAsync(new CommandDefinition(ensureCompanySql, uniqueCompanies, tx, cancellationToken: cancellationToken));

        const string sql = @"
            INSERT INTO Exceptions (
                Id, CompanyId, RuleId, RuleName, Category, Severity, 
                EntityId, EntityType, VoucherId, LedgerId, VoucherNumber, VoucherDate, 
                FlaggedAmount, Explanation, EvidenceJson, SuggestedCorrection, 
                Status, AuditorNote, AuditorAssignedTo, FlaggedAt, ReviewedAt
            )
            VALUES (
                @ResultId, @CompanyId, @RuleId, @RuleName, @Category, @Severity, 
                @EntityId, @EntityType, @VoucherId, @LedgerId, @VoucherNumber, @VoucherDate, 
                @FlaggedAmount, @Explanation, @Evidence, @SuggestedCorrection, 
                @Status, @ReviewerNote, @Reviewer, @DetectedAt, @ReviewedAt
            )
            ON CONFLICT(Id) DO UPDATE SET
                RuleName = excluded.RuleName,
                Category = excluded.Category,
                Severity = excluded.Severity,
                EntityId = excluded.EntityId,
                EntityType = excluded.EntityType,
                VoucherId = excluded.VoucherId,
                LedgerId = excluded.LedgerId,
                VoucherNumber = excluded.VoucherNumber,
                VoucherDate = excluded.VoucherDate,
                FlaggedAmount = excluded.FlaggedAmount,
                Explanation = excluded.Explanation,
                EvidenceJson = excluded.EvidenceJson,
                SuggestedCorrection = excluded.SuggestedCorrection;
        ";

        var parametersList = results.Select(r =>
        {
            var stableResultId = CreateStableFindingId(r);
            r.ResultId = stableResultId;

            return new
            {
                ResultId = stableResultId,
                r.CompanyId,
                r.RuleId,
                r.RuleName,
                Category = (int)r.Category,
                Severity = (int)r.Severity,
                EntityId = r.VoucherId ?? r.LedgerId ?? stableResultId,
                EntityType = !string.IsNullOrEmpty(r.VoucherId)
                    ? "Voucher"
                    : !string.IsNullOrEmpty(r.LedgerId) ? "Ledger" : "Finding",
                r.VoucherId,
                r.LedgerId,
                r.VoucherNumber,
                r.VoucherDate,
                r.FlaggedAmount,
                r.Explanation,
                r.Evidence,
                SuggestedCorrection = r.Explanation,
                Status = (int)r.Status,
                r.ReviewerNote,
                r.Reviewer,
                r.DetectedAt,
                ReviewedAt = r.Status != ReviewStatus.Pending ? DateTime.UtcNow : (DateTime?)null
            };
        });

        await connection.ExecuteAsync(new CommandDefinition(sql, parametersList, tx, cancellationToken: cancellationToken));
        tx.Commit();

        if (_auditTrailService != null)
        {
            var first = results.FirstOrDefault();
            _ = _auditTrailService.RecordActivityAsync(
                actionType: "Finding created",
                module: "AUDIT",
                description: $"Recorded {results.Count} audit finding(s) across rules (e.g. {first?.RuleName ?? "Rule"}).",
                companyName: first?.CompanyId,
                ct: CancellationToken.None);
        }
    }

    private static string CreateStableFindingId(AuditResult result)
    {
        // Exclude generated timestamps and review metadata so an unchanged finding
        // receives the same identity on every audit run. Any changed audit evidence
        // produces a new pending finding that must be reviewed again.
        var identity = new
        {
            CompanyId = result.CompanyId ?? string.Empty,
            RuleId = result.RuleId ?? string.Empty,
            Category = (int)result.Category,
            Severity = (int)result.Severity,
            VoucherId = result.VoucherId ?? string.Empty,
            LedgerId = result.LedgerId ?? string.Empty,
            VoucherNumber = result.VoucherNumber ?? string.Empty,
            VoucherDate = result.VoucherDate?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            FlaggedAmount = result.FlaggedAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            Explanation = result.Explanation ?? string.Empty,
            Evidence = string.IsNullOrWhiteSpace(result.Evidence) ? "{}" : result.Evidence
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(identity);
        return "FND-" + Convert.ToHexString(SHA256.HashData(payload));
    }

    public async Task<IReadOnlyList<AuditResult>> GetResultsAsync(string companyId, string? ruleId = null, SeverityLevel? minSeverity = null, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var sql = ExceptionQuerySql.RankedFindingsCte + @"
            SELECT 
                e.Id AS ResultId, e.RuleId, e.RuleName, e.CompanyId, e.VoucherId, e.LedgerId, 
                e.VoucherNumber, e.VoucherDate, e.FlaggedAmount, e.FlaggedAt AS DetectedAt, 
                e.Category, e.Severity, e.Explanation, e.EvidenceJson AS Evidence, e.Status, 
                e.AuditorAssignedTo AS Reviewer, e.AuditorNote AS ReviewerNote 
            FROM Exceptions e
            INNER JOIN RankedFindings ranked ON ranked.Id = e.Id AND ranked.DuplicateRank = 1
            WHERE e.CompanyId = @CompanyId
        ";

        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId);

        if (!string.IsNullOrEmpty(ruleId))
        {
            sql += " AND e.RuleId = @RuleId";
            parameters.Add("RuleId", ruleId);
        }

        if (minSeverity.HasValue)
        {
            sql += " AND e.Severity >= @Severity";
            parameters.Add("Severity", (int)minSeverity.Value);
        }

        sql += " ORDER BY e.Severity DESC, e.FlaggedAt DESC;";

        var results = await connection.QueryAsync<AuditResult>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        return results.ToList();
    }

    public async Task ClearResultsForCompanyAsync(string companyId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "DELETE FROM Exceptions WHERE CompanyId = @CompanyId;";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: cancellationToken));
    }
}

public class ExceptionRepository : IExceptionRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public ExceptionRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AuditResult>> GetExceptionsAsync(
        string companyId, 
        RuleCategory? category = null, 
        SeverityLevel? minSeverity = null, 
        ReviewStatus? status = null, 
        int skip = 0, 
        int take = 50, 
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var sql = ExceptionQuerySql.RankedFindingsCte + @"
            SELECT 
                e.Id AS ResultId, e.RuleId, e.RuleName, e.CompanyId, e.VoucherId, e.LedgerId, 
                e.VoucherNumber, e.VoucherDate, e.FlaggedAmount, e.FlaggedAt AS DetectedAt, 
                e.Category, e.Severity, e.Explanation, e.EvidenceJson AS Evidence, e.Status, 
                e.AuditorAssignedTo AS Reviewer, e.AuditorNote AS ReviewerNote 
            FROM Exceptions e
            INNER JOIN RankedFindings ranked ON ranked.Id = e.Id AND ranked.DuplicateRank = 1
            WHERE e.CompanyId = @CompanyId
        ";

        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId);

        if (category.HasValue)
        {
            sql += " AND e.Category = @Category";
            parameters.Add("Category", (int)category.Value);
        }

        if (minSeverity.HasValue)
        {
            sql += " AND e.Severity >= @Severity";
            parameters.Add("Severity", (int)minSeverity.Value);
        }

        if (status.HasValue)
        {
            sql += " AND e.Status = @Status";
            parameters.Add("Status", (int)status.Value);
        }

        sql += " ORDER BY e.Severity DESC, e.VoucherDate DESC LIMIT @Take OFFSET @Skip;";
        parameters.Add("Take", take);
        parameters.Add("Skip", skip);

        var results = await connection.QueryAsync<AuditResult>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        return results.ToList();
    }

    public async Task<int> GetExceptionCountAsync(
        string companyId, 
        RuleCategory? category = null, 
        SeverityLevel? minSeverity = null, 
        ReviewStatus? status = null, 
        CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var sql = ExceptionQuerySql.RankedFindingsCte + @"
            SELECT COUNT(*)
            FROM Exceptions e
            INNER JOIN RankedFindings ranked ON ranked.Id = e.Id AND ranked.DuplicateRank = 1
            WHERE e.CompanyId = @CompanyId";
        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId);

        if (category.HasValue)
        {
            sql += " AND e.Category = @Category";
            parameters.Add("Category", (int)category.Value);
        }

        if (minSeverity.HasValue)
        {
            sql += " AND e.Severity >= @Severity";
            parameters.Add("Severity", (int)minSeverity.Value);
        }
        if (status.HasValue)
        {
            sql += " AND e.Status = @Status";
            parameters.Add("Status", (int)status.Value);
        }

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    }

    public async Task UpdateStatusAsync(string resultId, ReviewStatus status, string? reviewer, string? reviewerNote, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = @"
            UPDATE Exceptions 
            SET Status = @Status,
                AuditorAssignedTo = COALESCE(@Reviewer, AuditorAssignedTo),
                AuditorNote = COALESCE(@ReviewerNote, AuditorNote),
                ReviewedAt = CURRENT_TIMESTAMP
            WHERE Id = @Id;
        ";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = resultId, Status = (int)status, Reviewer = reviewer, ReviewerNote = reviewerNote }, cancellationToken: cancellationToken));
    }
}


internal static class ExceptionQuerySql
{
    internal const string RankedFindingsCte = @"
WITH RankedFindings AS (
    SELECT
        Id,
        ROW_NUMBER() OVER (
            PARTITION BY
                CompanyId,
                RuleId,
                Category,
                Severity,
                COALESCE(VoucherId, ''),
                COALESCE(LedgerId, ''),
                COALESCE(VoucherNumber, ''),
                COALESCE(VoucherDate, ''),
                COALESCE(FlaggedAmount, 0),
                COALESCE(Explanation, ''),
                COALESCE(EvidenceJson, '{}'),
                CASE
                    WHEN COALESCE(VoucherId, '') <> '' THEN 'V:' || VoucherId
                    WHEN COALESCE(LedgerId, '') <> '' THEN 'L:' || LedgerId
                    ELSE 'I:' || COALESCE(EntityId, Id)
                END
            ORDER BY
                CASE WHEN Status = 0 THEN 1 ELSE 0 END,
                COALESCE(ReviewedAt, FlaggedAt) DESC,
                FlaggedAt DESC,
                Id DESC
        ) AS DuplicateRank
    FROM Exceptions
    WHERE CompanyId = @CompanyId
)
";
}
