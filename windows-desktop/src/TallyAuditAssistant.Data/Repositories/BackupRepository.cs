using Dapper;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Backup;

namespace TallyAuditAssistant.Data.Repositories;

public interface IBackupRepository
{
    Task<bool> InsertAsync(BackupMetadata metadata, CancellationToken ct = default);
    Task<IReadOnlyList<BackupMetadata>> GetAllAsync(CancellationToken ct = default);
}

public class BackupRepository : IBackupRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<BackupRepository> _logger;

    public BackupRepository(SqliteConnectionFactory connectionFactory, ILogger<BackupRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<bool> InsertAsync(BackupMetadata metadata, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = @"
            INSERT INTO BackupHistory (Id, Timestamp, FilePath, FileSizeBytes, RecordCount, TriggerReason, ChecksumSha256, IsValidated)
            VALUES (@Id, @Timestamp, @FilePath, @FileSizeBytes, @RecordCount, @TriggerReason, @ChecksumSha256, @IsValidated);";

        var rows = await connection.ExecuteAsync(new CommandDefinition(sql, metadata, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<IReadOnlyList<BackupMetadata>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        const string sql = "SELECT * FROM BackupHistory ORDER BY Timestamp DESC;";
        var results = await connection.QueryAsync<BackupMetadata>(new CommandDefinition(sql, cancellationToken: ct));
        return results.ToList();
    }
}
