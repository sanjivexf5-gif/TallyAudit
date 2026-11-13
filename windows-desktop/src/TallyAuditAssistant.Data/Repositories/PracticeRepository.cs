using Dapper;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Practice;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data.Repositories;

/// <summary>
/// SQLite persistence for practice management. Queries are bounded to the
/// requested client/status and ordered consistently for predictable UI loads.
/// </summary>
public sealed class PracticeRepository : IPracticeRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<PracticeRepository> _logger;

    public PracticeRepository(SqliteConnectionFactory connectionFactory, ILogger<PracticeRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PracticeClient>> GetClientsAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"SELECT * FROM PracticeClients
            WHERE (@IncludeInactive = 1 OR Status = 'Active')
            ORDER BY DisplayName COLLATE NOCASE;";
        var rows = await connection.QueryAsync<PracticeClient>(new CommandDefinition(sql, new { IncludeInactive = includeInactive }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<PracticeClient?> GetClientByIdAsync(string id, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<PracticeClient>(
            new CommandDefinition("SELECT * FROM PracticeClients WHERE Id = @Id;", new { Id = id }, cancellationToken: ct));
    }

    public async Task<bool> CreateClientAsync(PracticeClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ValidateClient(client);
        client.Id = string.IsNullOrWhiteSpace(client.Id) ? Guid.NewGuid().ToString() : client.Id;
        client.CreatedAt = client.CreatedAt == default ? DateTime.UtcNow : client.CreatedAt;
        client.UpdatedAt = DateTime.UtcNow;
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"INSERT INTO PracticeClients
            (Id, ClientCode, DisplayName, ClientType, PAN, Email, Phone, Address, Status, Notes, CreatedAt, UpdatedAt)
            VALUES (@Id, @ClientCode, @DisplayName, @ClientType, @PAN, @Email, @Phone, @Address, @Status, @Notes, @CreatedAt, @UpdatedAt);";
        try
        {
            return await connection.ExecuteAsync(new CommandDefinition(sql, client, cancellationToken: ct)) == 1;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to create practice client {ClientCode}", client.ClientCode);
            throw;
        }
    }

    public async Task<bool> UpdateClientAsync(PracticeClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ValidateClient(client);
        client.UpdatedAt = DateTime.UtcNow;
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"UPDATE PracticeClients SET ClientCode=@ClientCode, DisplayName=@DisplayName,
            ClientType=@ClientType, PAN=@PAN, Email=@Email, Phone=@Phone, Address=@Address,
            Status=@Status, Notes=@Notes, UpdatedAt=@UpdatedAt WHERE Id=@Id;";
        return await connection.ExecuteAsync(new CommandDefinition(sql, client, cancellationToken: ct)) == 1;
    }

    public async Task<IReadOnlyList<PracticeClientEntity>> GetClientEntitiesAsync(string clientId, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        var rows = await connection.QueryAsync<PracticeClientEntity>(new CommandDefinition(
            "SELECT * FROM PracticeClientEntities WHERE ClientId=@ClientId ORDER BY LegalName COLLATE NOCASE;",
            new { ClientId = clientId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> CreateClientEntityAsync(PracticeClientEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ValidateEntity(entity);
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"INSERT INTO PracticeClientEntities
            (Id, ClientId, LegalName, PAN, GSTIN, StateCode, TallyCompanyId, Status, CreatedAt)
            VALUES (@Id, @ClientId, @LegalName, @PAN, @GSTIN, @StateCode, @TallyCompanyId, @Status, @CreatedAt);";
        return await connection.ExecuteAsync(new CommandDefinition(sql, entity, cancellationToken: ct)) == 1;
    }

    public async Task<bool> UpdateClientEntityAsync(PracticeClientEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ValidateEntity(entity);
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"UPDATE PracticeClientEntities SET LegalName=@LegalName, PAN=@PAN, GSTIN=@GSTIN,
            StateCode=@StateCode, TallyCompanyId=@TallyCompanyId, Status=@Status WHERE Id=@Id AND ClientId=@ClientId;";
        return await connection.ExecuteAsync(new CommandDefinition(sql, entity, cancellationToken: ct)) == 1;
    }

    public async Task<IReadOnlyList<ClientEngagement>> GetEngagementsAsync(string? clientId = null, string? status = null, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"SELECT * FROM ClientEngagements
            WHERE (@ClientId IS NULL OR ClientId=@ClientId)
              AND (@Status IS NULL OR Status=@Status)
            ORDER BY CASE WHEN DueDate IS NULL THEN 1 ELSE 0 END, DueDate, CreatedAt DESC;";
        var rows = await connection.QueryAsync<ClientEngagement>(new CommandDefinition(sql,
            new { ClientId = NullIfWhiteSpace(clientId), Status = NullIfWhiteSpace(status) }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<ClientEngagement?> GetEngagementByIdAsync(string id, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<ClientEngagement>(new CommandDefinition(
            "SELECT * FROM ClientEngagements WHERE Id=@Id;", new { Id = id }, cancellationToken: ct));
    }

    public async Task<bool> CreateEngagementAsync(ClientEngagement engagement, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engagement);
        ValidateEngagement(engagement);
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"INSERT INTO ClientEngagements
            (Id, ClientId, ClientEntityId, ServiceCode, ServiceName, FinancialYear, Status,
             AssignedToUserId, ReviewerUserId, DueDate, ProfessionalFee, Notes, CreatedAt, UpdatedAt)
            VALUES (@Id, @ClientId, @ClientEntityId, @ServiceCode, @ServiceName, @FinancialYear, @Status,
             @AssignedToUserId, @ReviewerUserId, @DueDate, @ProfessionalFee, @Notes, @CreatedAt, @UpdatedAt);";
        return await connection.ExecuteAsync(new CommandDefinition(sql, engagement, cancellationToken: ct)) == 1;
    }

    public async Task<bool> UpdateEngagementAsync(ClientEngagement engagement, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engagement);
        ValidateEngagement(engagement);
        engagement.UpdatedAt = DateTime.UtcNow;
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"UPDATE ClientEngagements SET ClientId=@ClientId, ClientEntityId=@ClientEntityId,
            ServiceCode=@ServiceCode, ServiceName=@ServiceName, FinancialYear=@FinancialYear, Status=@Status,
            AssignedToUserId=@AssignedToUserId, ReviewerUserId=@ReviewerUserId, DueDate=@DueDate,
            ProfessionalFee=@ProfessionalFee, Notes=@Notes, UpdatedAt=@UpdatedAt WHERE Id=@Id;";
        return await connection.ExecuteAsync(new CommandDefinition(sql, engagement, cancellationToken: ct)) == 1;
    }

    private static void ValidateClient(PracticeClient client)
    {
        if (string.IsNullOrWhiteSpace(client.ClientCode)) throw new ArgumentException("Client code is required.", nameof(client));
        if (string.IsNullOrWhiteSpace(client.DisplayName)) throw new ArgumentException("Client name is required.", nameof(client));
        if (client.ClientCode.Length > 40 || client.DisplayName.Length > 200) throw new ArgumentException("Client code or name exceeds the allowed length.", nameof(client));
        if (client.PAN is { Length: > 10 }) throw new ArgumentException("PAN cannot exceed 10 characters.", nameof(client));
        if (client.Status is not ("Active" or "Inactive")) throw new ArgumentException("Client status must be Active or Inactive.", nameof(client));
    }

    private static void ValidateEntity(PracticeClientEntity entity)
    {
        if (string.IsNullOrWhiteSpace(entity.ClientId) || string.IsNullOrWhiteSpace(entity.LegalName))
            throw new ArgumentException("Client and legal entity name are required.", nameof(entity));
        if (entity.PAN is { Length: > 10 }) throw new ArgumentException("PAN cannot exceed 10 characters.", nameof(entity));
        if (entity.GSTIN is { Length: > 15 }) throw new ArgumentException("GSTIN cannot exceed 15 characters.", nameof(entity));
        if (entity.Status is not ("Active" or "Inactive")) throw new ArgumentException("Entity status must be Active or Inactive.", nameof(entity));
    }

    private static void ValidateEngagement(ClientEngagement engagement)
    {
        if (string.IsNullOrWhiteSpace(engagement.ClientId) || string.IsNullOrWhiteSpace(engagement.ServiceCode)
            || string.IsNullOrWhiteSpace(engagement.ServiceName) || string.IsNullOrWhiteSpace(engagement.FinancialYear))
            throw new ArgumentException("Client, service and financial year are required.", nameof(engagement));
        if (engagement.ProfessionalFee.HasValue && engagement.ProfessionalFee.Value < 0m) throw new ArgumentOutOfRangeException(nameof(engagement), "Professional fee cannot be negative.");
        if (engagement.Status is not ("Open" or "In Progress" or "Awaiting Client" or "Under Review" or "Completed" or "On Hold" or "Cancelled"))
            throw new ArgumentException("Engagement status is not supported.", nameof(engagement));
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
