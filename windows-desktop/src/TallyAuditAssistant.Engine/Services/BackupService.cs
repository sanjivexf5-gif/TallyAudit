using System.IO;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Backup;
using TallyAuditAssistant.Core.Domain.Security;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;

namespace TallyAuditAssistant.Engine.Services;

public class BackupService : IBackupService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IBackupRepository _backupRepository;
    private readonly IAuditTrailService _auditTrailService;
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<BackupService> _logger;
    private readonly string _databaseFilePath;
    private string _backupDirectoryPath;

    public BackupService(
        SqliteConnectionFactory connectionFactory,
        IBackupRepository backupRepository,
        IAuditTrailService auditTrailService,
        IAuthorizationService authorizationService,
        ILogger<BackupService> logger,
        string databaseFilePath,
        string backupDirectoryPath)
    {
        _connectionFactory = connectionFactory;
        _backupRepository = backupRepository;
        _auditTrailService = auditTrailService;
        _authorizationService = authorizationService;
        _logger = logger;
        _databaseFilePath = databaseFilePath;
        _backupDirectoryPath = backupDirectoryPath;

        if (!Directory.Exists(_backupDirectoryPath))
        {
            Directory.CreateDirectory(_backupDirectoryPath);
        }
    }

    public string BackupDirectoryPath
    {
        get => _backupDirectoryPath;
        set
        {
            _backupDirectoryPath = value;
            if (!Directory.Exists(_backupDirectoryPath))
            {
                Directory.CreateDirectory(_backupDirectoryPath);
            }
        }
    }

    public async Task<BackupMetadata> CreateBackupAsync(string triggerReason, CancellationToken ct = default)
    {
        _logger.LogInformation("Initiating database backup. Trigger: {Reason}", triggerReason);

        if (!File.Exists(_databaseFilePath))
        {
            throw new FileNotFoundException("Primary SQLite database file not found.", _databaseFilePath);
        }

        string timestampStr = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string backupFileName = $"TallyAudit_Backup_{timestampStr}.db.bak";
        string targetFilePath = Path.Combine(_backupDirectoryPath, backupFileName);
        string tempFilePath = targetFilePath + ".tmp";

        int recordCount = 0;
        using (var connection = await _connectionFactory.CreateConnectionAsync(ct))
        {
            // Force WAL checkpoint to flush pending transactions to main database file safely
            await connection.ExecuteAsync(new CommandDefinition("PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken: ct));

            try
            {
                recordCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM Vouchers;", cancellationToken: ct));
            }
            catch
            {
                recordCount = 0;
            }
        }

        // Use SQLite's online backup API. Copying only the main .db file can omit
        // committed pages that are still in the WAL when another connection is active.
        if (File.Exists(tempFilePath))
        {
            File.Delete(tempFilePath);
        }

        using (var source = await _connectionFactory.CreateConnectionAsync(ct))
        {
            var destinationBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = tempFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            };

            using var destination = new SqliteConnection(destinationBuilder.ToString());
            await destination.OpenAsync(ct);
            source.BackupDatabase(destination);
        }

        if (!await ValidateBackupIntegrityAsync(tempFilePath, ct))
        {
            try { File.Delete(tempFilePath); } catch { }
            throw new InvalidDataException("SQLite backup failed its integrity check. The backup was not published.");
        }

        File.Move(tempFilePath, targetFilePath, overwrite: true);

        var fileInfo = new FileInfo(targetFilePath);
        string checksum = ComputeFileSha256(targetFilePath);

        var metadata = new BackupMetadata
        {
            Id = $"BAK-{timestampStr}-{Guid.NewGuid().ToString()[..4]}",
            Timestamp = DateTime.UtcNow,
            FilePath = targetFilePath,
            FileSizeBytes = fileInfo.Length,
            RecordCount = recordCount,
            TriggerReason = triggerReason,
            ChecksumSha256 = checksum,
            IsValidated = true
        };

        await _backupRepository.InsertAsync(metadata, ct);

        await _auditTrailService.RecordActivityAsync(
            actionType: "Database Backup Created",
            module: "BACKUP_RESTORE",
            description: $"Backup created: {backupFileName} (Size: {fileInfo.Length / 1024} KB, Checksum: {checksum[..12]}...)",
            ct: ct);

        _logger.LogInformation("Database backup created successfully at {Path}", targetFilePath);
        return metadata;
    }

    public async Task<bool> RestoreBackupAsync(string backupFilePath, CancellationToken ct = default)
    {
        _authorizationService.DemandPermission(AuditPermission.BackupRestore);

        _logger.LogWarning("Initiating database restore from {BackupPath}", backupFilePath);

        if (!File.Exists(backupFilePath))
            throw new FileNotFoundException("Backup file not found.", backupFilePath);

        bool isValid = await ValidateBackupIntegrityAsync(backupFilePath, ct);
        if (!isValid)
            throw new InvalidOperationException("Backup integrity validation failed. Restore aborted.");

        // 1. Create safety snapshot of current state before overwrite
        await CreateBackupAsync("Pre-Restore Safety Snapshot", ct);

        // 2. Overwrite database file atomically
        string tempRestorePath = _databaseFilePath + ".restoring";
        File.Copy(backupFilePath, tempRestorePath, overwrite: true);
        File.Move(tempRestorePath, _databaseFilePath, overwrite: true);

        await _auditTrailService.RecordActivityAsync(
            actionType: "Database Restored",
            module: "BACKUP_RESTORE",
            description: $"Database restored from backup file: {Path.GetFileName(backupFilePath)}",
            ct: ct);

        _logger.LogInformation("Database successfully restored from {BackupPath}", backupFilePath);
        return true;
    }

    public Task<IReadOnlyList<BackupMetadata>> GetBackupHistoryAsync(CancellationToken ct = default)
    {
        return _backupRepository.GetAllAsync(ct);
    }

    public async Task<bool> ValidateBackupIntegrityAsync(string backupFilePath, CancellationToken ct = default)
    {
        if (!File.Exists(backupFilePath))
            return false;

        try
        {
            ct.ThrowIfCancellationRequested();

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = Path.GetFullPath(backupFilePath),
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            };

            using var connection = new SqliteConnection(builder.ToString());
            await connection.OpenAsync(ct);

            // The SQLite file signature alone does not prove the page tree is usable.
            // Require a clean integrity_check and the core tables expected by this app.
            var integrityRows = (await connection.QueryAsync<string>(
                new CommandDefinition("PRAGMA integrity_check;", cancellationToken: ct))).ToList();

            if (integrityRows.Count != 1 ||
                !string.Equals(integrityRows[0], "ok", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Backup integrity check failed for {Path}: {Result}",
                    backupFilePath,
                    string.Join("; ", integrityRows.Take(3)));
                return false;
            }

            var coreTableCount = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Companies', 'Vouchers');",
                    cancellationToken: ct));

            return coreTableCount == 2;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed validating backup file {Path}", backupFilePath);
            return false;
        }
    }

    private static string ComputeFileSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
