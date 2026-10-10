using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine.Services;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class BackupServiceIntegrityTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"taa-backup-tests-{Guid.NewGuid():N}");
    private string DatabasePath => Path.Combine(_root, "audit.db");
    private string BackupDirectory => Path.Combine(_root, "backups");
    private SqliteConnectionFactory _factory = null!;
    private DatabaseInitializer _initializer = null!;
    private BackupService _service = null!;

    public BackupServiceIntegrityTests()
    {
        Directory.CreateDirectory(_root);
        _factory = new SqliteConnectionFactory(DatabasePath);
        _initializer = new DatabaseInitializer(
            _factory,
            NullLogger<DatabaseInitializer>.Instance,
            DatabasePath);

        _service = new BackupService(
            _factory,
            new Mock<IBackupRepository>().Object,
            new Mock<IAuditTrailService>().Object,
            new Mock<IAuthorizationService>().Object,
            NullLogger<BackupService>.Instance,
            DatabasePath,
            BackupDirectory);
    }

    public async Task InitializeAsync()
    {
        await _initializer.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // SQLite can keep short-lived WAL sidecar handles open briefly on Windows.
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ValidateBackupIntegrity_RejectsFileWithOnlySQLiteSignature()
    {
        var corruptPath = Path.Combine(_root, "header-only.db.bak");
        var bytes = new byte[512];
        Encoding.ASCII.GetBytes("SQLite format 3\0").CopyTo(bytes, 0);
        File.WriteAllBytes(corruptPath, bytes);

        Assert.False(await _service.ValidateBackupIntegrityAsync(corruptPath));
    }

    [Fact]
    public async Task CreateBackup_CreatesConsistentSnapshotAndPassesIntegrityCheck()
    {
        using (var connection = await _factory.CreateConnectionAsync())
        {
            await connection.ExecuteAsync(
                "INSERT INTO Companies (Id, TallyCompanyName, BooksFromDate) VALUES ('COMP-BACKUP', 'Backup Test Co', '2025-04-01');");
        }

        var metadata = await _service.CreateBackupAsync("Integrity regression test");

        Assert.True(metadata.IsValidated);
        Assert.True(File.Exists(metadata.FilePath));
        Assert.True(await _service.ValidateBackupIntegrityAsync(metadata.FilePath));

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = metadata.FilePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        using var backupConnection = new SqliteConnection(connectionString);
        await backupConnection.OpenAsync();
        var companyName = await backupConnection.ExecuteScalarAsync<string>(
            "SELECT TallyCompanyName FROM Companies WHERE Id = 'COMP-BACKUP';");

        Assert.Equal("Backup Test Co", companyName);
    }
}
