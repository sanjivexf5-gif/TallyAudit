using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Data.Repositories;

public interface IAuditTrailRepository
{
    Task<bool> InsertAsync(AuditTrailEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditTrailEntry>> GetEntriesAsync(
        string? searchTerm = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        string? companyName = null,
        string? module = null,
        string? actionType = null,
        string? userName = null,
        int limit = 1000,
        CancellationToken ct = default);
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
            INSERT INTO AuditTrail (
                Id, TimestampUtc, UserName, ActionType, Module, 
                CompanyName, FinancialYear, EntityType, EntityId, 
                PreviousState, NewState, Description, Details, 
                ApplicationVersion, MachineName
            )
            VALUES (
                @Id, @TimestampUtc, @UserName, @ActionType, @Module, 
                @CompanyName, @FinancialYear, @EntityType, @EntityId, 
                @PreviousState, @NewState, @Description, @Details, 
                @ApplicationVersion, @MachineName
            );";

        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            entry.Id,
            entry.TimestampUtc,
            entry.UserName,
            entry.ActionType,
            entry.Module,
            entry.CompanyName,
            entry.FinancialYear,
            entry.EntityType,
            entry.EntityId,
            entry.PreviousState,
            entry.NewState,
            entry.Description,
            entry.Details,
            entry.ApplicationVersion,
            entry.MachineName
        }, cancellationToken: ct));

        return rows > 0;
    }

    public async Task<IReadOnlyList<AuditTrailEntry>> GetEntriesAsync(
        string? searchTerm = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        string? companyName = null,
        string? module = null,
        string? actionType = null,
        string? userName = null,
        int limit = 1000,
        CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);

        var sb = new StringBuilder();
        sb.Append(@"
            SELECT 
                Id, TimestampUtc, UserName, ActionType, Module,
                CompanyName, FinancialYear, EntityType, EntityId,
                PreviousState, NewState, Description, Details,
                ApplicationVersion, MachineName
            FROM AuditTrail
            WHERE 1=1 ");

        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            sb.Append(@" AND (
                Description LIKE @SearchPattern 
                OR Details LIKE @SearchPattern 
                OR EntityId LIKE @SearchPattern 
                OR EntityType LIKE @SearchPattern
                OR ActionType LIKE @SearchPattern
                OR Module LIKE @SearchPattern
                OR UserName LIKE @SearchPattern
                OR CompanyName LIKE @SearchPattern
            )");
            parameters.Add("SearchPattern", $"%{searchTerm.Trim()}%");
        }

        if (fromUtc.HasValue)
        {
            sb.Append(" AND TimestampUtc >= @FromUtc");
            parameters.Add("FromUtc", fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            sb.Append(" AND TimestampUtc <= @ToUtc");
            parameters.Add("ToUtc", toUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(companyName) && !companyName.Equals("All Companies", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(" AND CompanyName = @CompanyName");
            parameters.Add("CompanyName", companyName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(module) && !module.Equals("All Modules", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(" AND Module = @Module");
            parameters.Add("Module", module.Trim());
        }

        if (!string.IsNullOrWhiteSpace(actionType) && !actionType.Equals("All Actions", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(" AND ActionType = @ActionType");
            parameters.Add("ActionType", actionType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(userName) && !userName.Equals("All Users", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(" AND UserName = @UserName");
            parameters.Add("UserName", userName.Trim());
        }

        sb.Append(" ORDER BY TimestampUtc DESC LIMIT @Limit;");
        parameters.Add("Limit", limit > 0 ? limit : 1000);

        var results = await connection.QueryAsync<AuditTrailEntry>(
            new CommandDefinition(sb.ToString(), parameters, cancellationToken: ct));

        return results.ToList();
    }

    public async Task<IReadOnlyList<AuditTrailEntry>> GetEntriesAsync(string? companyId = null, string? financialPeriodId = null, int limit = 100, CancellationToken ct = default)
    {
        return await GetEntriesAsync(
            searchTerm: null,
            fromUtc: null,
            toUtc: null,
            companyName: companyId,
            module: null,
            actionType: null,
            userName: null,
            limit: limit,
            ct: ct);
    }
}

