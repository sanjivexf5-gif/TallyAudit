using Dapper;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Data.Repositories;

public interface IAuditTrailRepository
{
    Task<bool> InsertAsync(AuditTrailEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditTrailEntry>> GetEntriesAsync(string? companyId = null, string? financialPeriodId = null, int limit = 100, CancellationToken ct = default);
}

public class AuditTrailRepository : IAuditTrailRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<AuditTrailRepository> _logger;

    public AuditTrailRepository(SqliteConnectionFactory connectionFactory, ILogger<AuditTrailRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<bool> InsertAsync(AuditTrailEntry entry, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"
            INSERT INTO AuditTrail (Id, Timestamp, Username, Action, Category, CompanyId, FinancialPeriodId, EntityType, EntityId, Description, MetadataJson, IntegrityHash)
            VALUES (@Id, @Timestamp, @Username, @Action, @Category, @CompanyId, @FinancialPeriodId, @EntityType, @EntityId, @Description, @MetadataJson, @IntegrityHash);";

        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, entry, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<IReadOnlyList<AuditTrailEntry>> GetEntriesAsync(string? companyId = null, string? financialPeriodId = null, int limit = 100, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        string sql = @"
            SELECT * FROM AuditTrail
            WHERE (@CompanyId IS NULL OR CompanyId = @CompanyId)
              AND (@FinancialPeriodId IS NULL OR FinancialPeriodId = @FinancialPeriodId)
            ORDER BY Timestamp DESC
            LIMIT @Limit;";

        var results = await connection.QueryAsync<AuditTrailEntry>(
            new CommandDefinition(sql, new { CompanyId = companyId, FinancialPeriodId = financialPeriodId, Limit = limit }, cancellationToken: ct));
        return results.ToList();
    }
}
