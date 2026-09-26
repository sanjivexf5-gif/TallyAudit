using System.IO;
using System.Security.Cryptography;
using Dapper;
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

        // Copy file safely via temporary file
        File.Copy(_databaseFilePath, tempFilePath, overwrite: true);
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
            action: "Database Backup Created",
            category: "BACKUP_RESTORE",
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
            action: "Database Restored",
            category: "BACKUP_RESTORE",
            description: $"Database restored from backup file: {Path.GetFileName(backupFilePath)}",
            ct: ct);

        _logger.LogInformation("Database successfully restored from {BackupPath}", backupFilePath);
        return true;
    }

    public Task<IReadOnlyList<BackupMetadata>> GetBackupHistoryAsync(CancellationToken ct = default)
    {
        return _backupRepository.GetAllAsync(ct);
    }

    public Task<bool> ValidateBackupIntegrityAsync(string backupFilePath, CancellationToken ct = default)
    {
        if (!File.Exists(backupFilePath))
            return Task.FromResult(false);

        try
        {
            var info = new FileInfo(backupFilePath);
            if (info.Length < 100) return Task.FromResult(false);

            // Verify SQLite file header: "SQLite format 3\0"
            byte[] header = new byte[16];
            using (var stream = File.OpenRead(backupFilePath))
            {
                int read = stream.Read(header, 0, 16);
                if (read < 16) return Task.FromResult(false);
            }

            string headerString = System.Text.Encoding.ASCII.GetString(header);
            return Task.FromResult(headerString.StartsWith("SQLite format 3"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed validating backup file {Path}", backupFilePath);
            return Task.FromResult(false);
        }
    }

    private static string ComputeFileSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
