using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Security;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data.Security;

namespace TallyAuditAssistant.Data.Migrations;

public class MigrationService : IMigrationService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<MigrationService> _logger;

    public MigrationService(SqliteConnectionFactory connectionFactory, ILogger<MigrationService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DatabaseMigrationInfo>> GetAppliedMigrationsAsync(CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await EnsureMigrationTableExistsAsync(connection, ct);

        const string sql = "SELECT * FROM __MigrationHistory ORDER BY AppliedAt ASC;";
        var list = await connection.QueryAsync<DatabaseMigrationInfo>(new CommandDefinition(sql, cancellationToken: ct));
        return list.ToList();
    }

    public async Task<string> GetCurrentSchemaVersionAsync(CancellationToken ct = default)
    {
        var migrations = await GetAppliedMigrationsAsync(ct);
        return migrations.Count > 0 ? migrations.Last().Version : "0.0.0";
    }

    public async Task<bool> ApplyPendingMigrationsAsync(CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        await EnsureMigrationTableExistsAsync(connection, ct);

        var applied = (await GetAppliedMigrationsAsync(ct)).Select(m => m.MigrationId).ToHashSet();

        var pendingMigrations = GetAvailableMigrations()
            .Where(m => !applied.Contains(m.MigrationId))
            .OrderBy(m => m.MigrationId)
            .ToList();

        if (pendingMigrations.Count == 0)
        {
            _logger.LogInformation("Database is up to date. No pending migrations.");
            return true;
        }

        _logger.LogInformation("Found {Count} pending migrations to apply.", pendingMigrations.Count);

        foreach (var migration in pendingMigrations)
        {
            _logger.LogInformation("Applying migration {Id}: {Description} (v{Version})", 
                migration.MigrationId, migration.Description, migration.Version);

            using var transaction = connection.BeginTransaction();
            try
            {
                await connection.ExecuteAsync(new CommandDefinition(migration.SqlScript, transaction: transaction, cancellationToken: ct));

                // Execute any programmatic step if provided (e.g., seeding initial admin)
                if (migration.PostActionAsync != null)
                {
                    await migration.PostActionAsync(connection, transaction, ct);
                }

                string checksum = ComputeChecksum(migration.SqlScript);
                const string insertMigrationSql = @"
                    INSERT INTO __MigrationHistory (MigrationId, Version, Description, AppliedAt, Checksum)
                    VALUES (@MigrationId, @Version, @Description, @AppliedAt, @Checksum);";

                await connection.ExecuteAsync(new CommandDefinition(insertMigrationSql, new
                {
                    migration.MigrationId,
                    migration.Version,
                    migration.Description,
                    AppliedAt = DateTime.UtcNow,
                    Checksum = checksum
                }, transaction: transaction, cancellationToken: ct));

                transaction.Commit();
                _logger.LogInformation("Migration {Id} applied successfully.", migration.MigrationId);
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogCritical(ex, "Failed applying migration {Id}. Database transaction rolled back.", migration.MigrationId);
                throw new InvalidOperationException($"Migration {migration.MigrationId} failed: {ex.Message}", ex);
            }
        }

        return true;
    }

    private static async Task EnsureMigrationTableExistsAsync(System.Data.Common.DbConnection connection, CancellationToken ct)
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS __MigrationHistory (
                MigrationId TEXT PRIMARY KEY,
                Version TEXT NOT NULL,
                Description TEXT NOT NULL,
                AppliedAt DATETIME NOT NULL,
                Checksum TEXT NOT NULL
            );";
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    private static string ComputeChecksum(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16];
    }

    private record MigrationDefinition(
        string MigrationId,
        string Version,
        string Description,
        string SqlScript,
        Func<System.Data.Common.DbConnection, System.Data.Common.DbTransaction, CancellationToken, Task>? PostActionAsync = null);

    private static IEnumerable<MigrationDefinition> GetAvailableMigrations()
    {
        yield return new MigrationDefinition(
            "001_InitialCoreSchema",
            "1.0.0",
            "Core accounting tables, vouchers, groups, ledgers, and audit rules",
            @"
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS Companies (
                Id TEXT PRIMARY KEY,
                TallyCompanyName TEXT NOT NULL,
                FormalName TEXT,
                GSTIN TEXT,
                PAN TEXT,
                StateName TEXT,
                StateCode TEXT,
                BooksFromDate DATE NOT NULL,
                LastSyncDate DATETIME,
                LastAlterId INTEGER DEFAULT 0,
                IsActive INTEGER DEFAULT 1,
                CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS FinancialYears (
                Id TEXT PRIMARY KEY,
                CompanyId TEXT NOT NULL REFERENCES Companies(Id) ON DELETE CASCADE,
                StartDate DATE NOT NULL,
                EndDate DATE NOT NULL,
                IsAudited INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Groups (
                Id TEXT PRIMARY KEY,
                CompanyId TEXT NOT NULL REFERENCES Companies(Id) ON DELETE CASCADE,
                Name TEXT NOT NULL,
                ParentName TEXT,
                PrimaryGroup TEXT,
                AlterId INTEGER
            );

            CREATE TABLE IF NOT EXISTS Ledgers (
                Id TEXT PRIMARY KEY,
                CompanyId TEXT NOT NULL REFERENCES Companies(Id) ON DELETE CASCADE,
                Name TEXT NOT NULL,
                ParentGroup TEXT NOT NULL,
                GSTIN TEXT,
                PAN TEXT,
                StateName TEXT,
                OpeningBalance DECIMAL(18,2) DEFAULT 0,
                ClosingBalance DECIMAL(18,2) DEFAULT 0,
                IsBillWise INTEGER DEFAULT 0,
                TaxType TEXT,
                HsnCode TEXT,
                GstRate DECIMAL(5,2),
                TdsRate DECIMAL(5,2),
                AlterId INTEGER
            );

            CREATE TABLE IF NOT EXISTS VoucherTypes (
                Id TEXT PRIMARY KEY,
                CompanyId TEXT NOT NULL REFERENCES Companies(Id) ON DELETE CASCADE,
                Name TEXT NOT NULL,
                ParentType TEXT NOT NULL,
                NumberingMethod TEXT DEFAULT 'Automatic'
            );

            CREATE TABLE IF NOT EXISTS Vouchers (
                Id TEXT PRIMARY KEY,
                CompanyId TEXT NOT NULL REFERENCES Companies(Id) ON DELETE CASCADE,
                VoucherTypeId TEXT NOT NULL,
                VoucherTypeName TEXT NOT NULL,
                VoucherNumber TEXT,
                ReferenceNumber TEXT,
                VoucherDate DATE NOT NULL,
                EffectiveDate DATE,
                Narration TEXT,
                TotalAmount DECIMAL(18,2) NOT NULL,
                IsCancelled INTEGER DEFAULT 0,
                IsOptional INTEGER DEFAULT 0,
                PartyLedgerName TEXT,
                AlterId INTEGER NOT NULL,
                CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS VoucherEntries (
                Id TEXT PRIMARY KEY,
                VoucherId TEXT NOT NULL REFERENCES Vouchers(Id) ON DELETE CASCADE,
                LedgerName TEXT NOT NULL,
                Amount DECIMAL(18,2) NOT NULL,
                IsDebit INTEGER NOT NULL,
                BillRefType TEXT,
                BillName TEXT
            );

            CREATE TABLE IF NOT EXISTS AuditRules (
                RuleId TEXT PRIMARY KEY,
                Category INTEGER NOT NULL,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                Severity INTEGER NOT NULL,
                SuggestedReview TEXT NOT NULL,
                ParametersJson TEXT,
                IsEnabled INTEGER DEFAULT 1,
                Version TEXT NOT NULL,
                EffectiveFrom DATE,
                EffectiveTo DATE
            );

            CREATE TABLE IF NOT EXISTS Exceptions (
                Id TEXT PRIMARY KEY,
                CompanyId TEXT NOT NULL REFERENCES Companies(Id) ON DELETE CASCADE,
                RuleId TEXT NOT NULL REFERENCES AuditRules(RuleId),
                RuleName TEXT NOT NULL,
                Category INTEGER NOT NULL,
                Severity INTEGER NOT NULL,
                EntityId TEXT,
                EntityType TEXT DEFAULT 'Voucher',
                VoucherId TEXT,
                LedgerId TEXT,
                VoucherNumber TEXT,
                VoucherDate DATE,
                LedgerName TEXT,
                FlaggedAmount DECIMAL(18,2),
                Explanation TEXT,
                EvidenceJson TEXT NOT NULL,
                SuggestedCorrection TEXT,
                Status INTEGER DEFAULT 0,
                AuditorNote TEXT,
                AuditorAssignedTo TEXT,
                FlaggedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                ReviewedAt DATETIME
            );

            CREATE TABLE IF NOT EXISTS SyncHistory (
                Id TEXT PRIMARY KEY,
                CompanyId TEXT NOT NULL,
                SyncType TEXT NOT NULL,
                VouchersFetched INTEGER DEFAULT 0,
                MastersFetched INTEGER DEFAULT 0,
                DurationMs INTEGER,
                Status TEXT NOT NULL,
                ErrorMessage TEXT,
                Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS AuditRuns (
                Id TEXT PRIMARY KEY,
                CompanyId TEXT NOT NULL,
                Period TEXT NOT NULL,
                StartTime DATETIME NOT NULL,
                EndTime DATETIME NOT NULL,
                TransactionsAnalysed INTEGER DEFAULT 0,
                FindingsGenerated INTEGER DEFAULT 0,
                Status TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_vouchers_comp_date ON Vouchers(CompanyId, VoucherDate);
            CREATE INDEX IF NOT EXISTS idx_vouchers_comp_type ON Vouchers(CompanyId, VoucherTypeName);
            CREATE INDEX IF NOT EXISTS idx_entries_voucher ON VoucherEntries(VoucherId);
            CREATE INDEX IF NOT EXISTS idx_entries_ledger ON VoucherEntries(LedgerName);
            CREATE INDEX IF NOT EXISTS idx_exceptions_comp_rule ON Exceptions(CompanyId, RuleId);
            CREATE INDEX IF NOT EXISTS idx_exceptions_status ON Exceptions(CompanyId, Status);
            ");

        yield return new MigrationDefinition(
            "002_UserAuthenticationAndRoles",
            "1.1.0",
            "AppUsers table with PBKDF2 salt hashing and role permissions",
            @"
            CREATE TABLE IF NOT EXISTS AppUsers (
                Id TEXT PRIMARY KEY,
                Username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                DisplayName TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                PasswordSalt TEXT NOT NULL,
                Role INTEGER NOT NULL DEFAULT 1,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt DATETIME NOT NULL,
                LastLoginAt DATETIME
            );

            CREATE INDEX IF NOT EXISTS idx_users_username ON AppUsers(Username);
            ",
            async (conn, tx, ct) =>
            {
                // Seed initial Admin user if none exists
                int userCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM AppUsers;", transaction: tx, cancellationToken: ct));
                if (userCount == 0)
                {
                    var (hash, salt) = PasswordHasher.HashPassword("Admin@123");
                    var admin = new AppUser
                    {
                        Id = "USER-ADMIN-01",
                        Username = "admin",
                        DisplayName = "Principal Statutory Auditor (Admin)",
                        PasswordHash = hash,
                        PasswordSalt = salt,
                        Role = UserRole.Administrator,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    const string insertAdminSql = @"
                        INSERT INTO AppUsers (Id, Username, DisplayName, PasswordHash, PasswordSalt, Role, IsActive, CreatedAt)
                        VALUES (@Id, @Username, @DisplayName, @PasswordHash, @PasswordSalt, @Role, @IsActive, @CreatedAt);";

                    await conn.ExecuteAsync(new CommandDefinition(insertAdminSql, admin, transaction: tx, cancellationToken: ct));
                }
            });

        yield return new MigrationDefinition(
            "003_AuditTrailAndSecurity",
            "1.2.0",
            "AuditTrail immutable logs with SHA-256 tamper-evident integrity hashes",
            @"
            CREATE TABLE IF NOT EXISTS AuditTrail (
                Id TEXT PRIMARY KEY,
                Timestamp DATETIME NOT NULL,
                Username TEXT NOT NULL,
                Action TEXT NOT NULL,
                Category TEXT NOT NULL,
                CompanyId TEXT,
                FinancialPeriodId TEXT,
                EntityType TEXT,
                EntityId TEXT,
                Description TEXT NOT NULL,
                MetadataJson TEXT,
                IntegrityHash TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_audittrail_comp_period ON AuditTrail(CompanyId, FinancialPeriodId, Timestamp);
            CREATE INDEX IF NOT EXISTS idx_audittrail_category ON AuditTrail(Category, Timestamp);
            ");

        yield return new MigrationDefinition(
            "004_BackupAndSnapshots",
            "1.3.0",
            "BackupHistory repository for SQLite snapshot management",
            @"
            CREATE TABLE IF NOT EXISTS BackupHistory (
                Id TEXT PRIMARY KEY,
                Timestamp DATETIME NOT NULL,
                FilePath TEXT NOT NULL,
                FileSizeBytes INTEGER NOT NULL,
                RecordCount INTEGER NOT NULL,
                TriggerReason TEXT NOT NULL,
                ChecksumSha256 TEXT NOT NULL,
                IsValidated INTEGER NOT NULL DEFAULT 1
            );

            CREATE INDEX IF NOT EXISTS idx_backups_timestamp ON BackupHistory(Timestamp DESC);
            ");

        yield return new MigrationDefinition(
            "005_EvidenceAndFinalization",
            "1.4.0",
            "Audit evidence register, working papers, and engagement finalization immutability",
            @"
            CREATE TABLE IF NOT EXISTS AuditPlans (
                Id TEXT PRIMARY KEY,
                CompanyId TEXT NOT NULL REFERENCES Companies(Id) ON DELETE CASCADE,
                FinancialPeriodId TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'In Progress',
                MaterialityAmount DECIMAL(18,2) NOT NULL DEFAULT 250000,
                PerformanceMateriality DECIMAL(18,2) NOT NULL DEFAULT 187500,
                TrivialThreshold DECIMAL(18,2) NOT NULL DEFAULT 12500,
                IsFinalized INTEGER NOT NULL DEFAULT 0,
                FinalizedAt DATETIME,
                FinalizedBy TEXT,
                AuditOpinion TEXT,
                CreatedAt DATETIME NOT NULL,
                UpdatedAt DATETIME NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AuditEvidence (
                Id TEXT PRIMARY KEY,
                PlanId TEXT NOT NULL REFERENCES AuditPlans(Id) ON DELETE CASCADE,
                AuditArea TEXT NOT NULL,
                ProcedureId TEXT,
                FindingId TEXT,
                EvidenceType TEXT NOT NULL,
                Description TEXT NOT NULL,
                ReferenceNumber TEXT,
                FileName TEXT,
                FilePath TEXT,
                FileHash TEXT,
                FileSizeBytes INTEGER,
                DateReceived DATE,
                UploadedAt DATETIME NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Received',
                AuditorRemarks TEXT
            );

            CREATE TABLE IF NOT EXISTS WorkingPapers (
                Id TEXT PRIMARY KEY,
                PlanId TEXT NOT NULL REFERENCES AuditPlans(Id) ON DELETE CASCADE,
                AuditArea TEXT NOT NULL,
                Title TEXT NOT NULL,
                Objective TEXT NOT NULL,
                ProcedurePerformed TEXT NOT NULL,
                Conclusion TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Draft',
                PreparedBy TEXT NOT NULL,
                PreparedDate DATE NOT NULL,
                ReviewedBy TEXT,
                ReviewedDate DATE
            );

            CREATE INDEX IF NOT EXISTS idx_evidence_plan ON AuditEvidence(PlanId, AuditArea);
            CREATE INDEX IF NOT EXISTS idx_wp_plan ON WorkingPapers(PlanId, AuditArea);
            ");

        yield return new MigrationDefinition(
            "006_AddTdsRateToLedgers",
            "1.5.0",
            "Ensure TdsRate column exists on Ledgers table",
            "SELECT 1;",
            async (conn, tx, ct) =>
            {
                var columns = await conn.QueryAsync<string>(
                    new CommandDefinition("SELECT name FROM pragma_table_info('Ledgers');", transaction: tx, cancellationToken: ct));
                if (!columns.Contains("TdsRate", StringComparer.OrdinalIgnoreCase))
                {
                    await conn.ExecuteAsync(
                        new CommandDefinition("ALTER TABLE Ledgers ADD COLUMN TdsRate DECIMAL(5,2);", transaction: tx, cancellationToken: ct));
                }
            });
    }
}
