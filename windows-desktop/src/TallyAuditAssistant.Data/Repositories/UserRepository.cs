using Dapper;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Security;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data.Repositories;

public class UserRepository : IUserRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(SqliteConnectionFactory connectionFactory, ILogger<UserRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<AppUser?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = "SELECT * FROM AppUsers WHERE Id = @Id;";
        return await connection.QuerySingleOrDefaultAsync<AppUser>(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    public async Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = "SELECT * FROM AppUsers WHERE Username = @Username COLLATE NOCASE;";
        return await connection.QuerySingleOrDefaultAsync<AppUser>(new CommandDefinition(sql, new { Username = username }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = "SELECT * FROM AppUsers ORDER BY Username;";
        var results = await connection.QueryAsync<AppUser>(new CommandDefinition(sql, cancellationToken: ct));
        return results.ToList();
    }

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = "SELECT COUNT(*) FROM AppUsers;";
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<bool> CreateAsync(AppUser user, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"
            INSERT INTO AppUsers (Id, Username, DisplayName, PasswordHash, PasswordSalt, Role, IsActive, CreatedAt, LastLoginAt)
            VALUES (@Id, @Username, @DisplayName, @PasswordHash, @PasswordSalt, @Role, @IsActive, @CreatedAt, @LastLoginAt);";

        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, user, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> UpdateAsync(AppUser user, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"
            UPDATE AppUsers
            SET DisplayName = @DisplayName, Role = @Role, IsActive = @IsActive
            WHERE Id = @Id;";

        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, user, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> UpdateLastLoginAsync(string id, DateTime loginTime, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = "UPDATE AppUsers SET LastLoginAt = @LastLoginAt WHERE Id = @Id;";
        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, LastLoginAt = loginTime }, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> UpdatePasswordAsync(string id, string passwordHash, string passwordSalt, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = "UPDATE AppUsers SET PasswordHash = @PasswordHash, PasswordSalt = @PasswordSalt WHERE Id = @Id;";
        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, PasswordHash = passwordHash, PasswordSalt = passwordSalt }, cancellationToken: ct));
        return rows > 0;
    }
}
