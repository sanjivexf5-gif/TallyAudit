using Dapper;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<AuditResultRepository> _logger;

    public AuditResultRepository(SqliteConnectionFactory connectionFactory, ILogger<AuditResultRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task SaveResultsBatchAsync(IReadOnlyList<AuditResult> results, CancellationToken cancellationToken = default)
    {
        if (results.Count == 0) return;

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var tx = connection.BeginTransaction();

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
                Severity = excluded.Severity,
                Explanation = excluded.Explanation,
                EvidenceJson = excluded.EvidenceJson,
                FlaggedAmount = excluded.FlaggedAmount,
                Status = excluded.Status;
        ";

        var parametersList = results.Select(r => new
        {
            r.ResultId,
            r.CompanyId,
            r.RuleId,
            r.RuleName,
            Category = (int)r.Severity, // fallback integer
            Severity = (int)r.Severity,
            EntityId = r.VoucherId ?? r.LedgerId ?? r.ResultId,
            EntityType = !string.IsNullOrEmpty(r.VoucherId) ? "Voucher" : "Ledger",
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
        });

        await connection.ExecuteAsync(new CommandDefinition(sql, parametersList, tx, cancellationToken: cancellationToken));
        tx.Commit();
    }

    public async Task<IReadOnlyList<AuditResult>> GetResultsAsync(string companyId, string? ruleId = null, SeverityLevel? minSeverity = null, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var sql = @"
            SELECT 
                Id AS ResultId, RuleId, RuleName, CompanyId, VoucherId, LedgerId, 
                VoucherNumber, VoucherDate, FlaggedAmount, FlaggedAt AS DetectedAt, 
                Severity, Explanation, EvidenceJson AS Evidence, Status, 
                AuditorAssignedTo AS Reviewer, AuditorNote AS ReviewerNote 
            FROM Exceptions 
            WHERE CompanyId = @CompanyId
        ";

        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId);

        if (!string.IsNullOrEmpty(ruleId))
        {
            sql += " AND RuleId = @RuleId";
            parameters.Add("RuleId", ruleId);
        }

        if (minSeverity.HasValue)
        {
            sql += " AND Severity >= @Severity";
            parameters.Add("Severity", (int)minSeverity.Value);
        }

        sql += " ORDER BY Severity DESC, FlaggedAt DESC;";

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
        var sql = @"
            SELECT 
                Id AS ResultId, RuleId, RuleName, CompanyId, VoucherId, LedgerId, 
                VoucherNumber, VoucherDate, FlaggedAmount, FlaggedAt AS DetectedAt, 
                Severity, Explanation, EvidenceJson AS Evidence, Status, 
                AuditorAssignedTo AS Reviewer, AuditorNote AS ReviewerNote 
            FROM Exceptions 
            WHERE CompanyId = @CompanyId
        ";

        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId);

        if (minSeverity.HasValue)
        {
            sql += " AND Severity >= @Severity";
            parameters.Add("Severity", (int)minSeverity.Value);
        }

        if (status.HasValue)
        {
            sql += " AND Status = @Status";
            parameters.Add("Status", (int)status.Value);
        }

        sql += " ORDER BY Severity DESC, VoucherDate DESC LIMIT @Take OFFSET @Skip;";
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
        var sql = "SELECT COUNT(*) FROM Exceptions WHERE CompanyId = @CompanyId";
        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId);

        if (minSeverity.HasValue)
        {
            sql += " AND Severity >= @Severity";
            parameters.Add("Severity", (int)minSeverity.Value);
        }
        if (status.HasValue)
        {
            sql += " AND Status = @Status";
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
