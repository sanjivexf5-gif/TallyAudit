using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data.Repositories;

public class SettingsRepository : ISettingsService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SettingsRepository> _logger;

    public SettingsRepository(SqliteConnectionFactory connectionFactory, ILogger<SettingsRepository>? logger = null)
    {
        _connectionFactory = connectionFactory;
        _logger = logger ?? NullLogger<SettingsRepository>.Instance;
    }

    public async Task<string> GetSettingAsync(string key, string defaultValue = "", CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        const string sql = "SELECT Value FROM Settings WHERE Key = @Key LIMIT 1";
        var val = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(sql, new { Key = key }, cancellationToken: cancellationToken));
        return val ?? defaultValue;
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        using var transaction = connection.BeginTransaction();
        const string sql = @"
            INSERT INTO Settings (Key, Value, UpdatedAt) 
            VALUES (@Key, @Value, CURRENT_TIMESTAMP)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value, UpdatedAt = CURRENT_TIMESTAMP;
        ";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Key = key, Value = value }, transaction: transaction, cancellationToken: cancellationToken));
        
        const string verifySql = "SELECT Value FROM Settings WHERE Key = @Key LIMIT 1";
        var persistedVal = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(verifySql, new { Key = key }, transaction: transaction, cancellationToken: cancellationToken));
        if (!string.Equals(persistedVal, value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Settings persistence verification failed for key '{key}'. Expected '{value}', persisted '{persistedVal}'. Database path: {_connectionFactory.DatabasePath}");
        }

        transaction.Commit();
        _logger.LogDebug("[SQLite] SetSetting {Key} = {Value} verified at {Path}", key, value, _connectionFactory.DatabasePath);
    }

    public async Task<int> GetTallyPortAsync()
    {
        var val = await GetSettingAsync("TallyPort", "9000");
        return int.TryParse(val, out var port) ? port : 9000;
    }

    public Task SetTallyPortAsync(int port) => SetSettingAsync("TallyPort", port.ToString());

    public Task<string> GetTallyHostAsync() => GetSettingAsync("TallyHost", "localhost");

    public Task SetTallyHostAsync(string host) => SetSettingAsync("TallyHost", host);

    public async Task<bool> IsMockModeEnabledAsync()
    {
        var val = await GetSettingAsync("IsMockModeEnabled", "false");
        return bool.TryParse(val, out var enabled) && enabled;
    }

    public Task SetMockModeEnabledAsync(bool enabled) => SetSettingAsync("IsMockModeEnabled", enabled.ToString().ToLower());
}
