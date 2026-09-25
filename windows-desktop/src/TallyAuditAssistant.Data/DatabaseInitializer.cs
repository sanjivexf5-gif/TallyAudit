using Dapper;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data;

public class DatabaseInitializer : IDatabaseInitializer
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly string _databasePath;

    public DatabaseInitializer(SqliteConnectionFactory connectionFactory, ILogger<DatabaseInitializer> logger, string databasePath)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _databasePath = databasePath;
    }

    public string DatabasePath => _databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing local SQLite audit database at {Path}", _databasePath);

        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await ApplyMigrationsAsync(cancellationToken);
        await SeedInitialRulesAsync(cancellationToken);
    }

    public async Task ApplyMigrationsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        const string schemaSql = @"
            -- 1. Configuration & Settings
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            -- 2. Companies & Financial Years
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

            -- 3. Groups & Ledgers Master
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
                AlterId INTEGER
            );

            -- 4. Voucher Types & Transactions
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

            -- 5. Audit Rules & Exception Catalog
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

            -- 6. Synchronization Logs
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

            -- 7. Performance Indexes
            CREATE INDEX IF NOT EXISTS idx_vouchers_comp_date ON Vouchers(CompanyId, VoucherDate);
            CREATE INDEX IF NOT EXISTS idx_vouchers_comp_type ON Vouchers(CompanyId, VoucherTypeName);
            CREATE INDEX IF NOT EXISTS idx_vouchers_alterid ON Vouchers(CompanyId, AlterId);
            CREATE INDEX IF NOT EXISTS idx_entries_voucher ON VoucherEntries(VoucherId);
            CREATE INDEX IF NOT EXISTS idx_entries_ledger ON VoucherEntries(LedgerName);
            CREATE INDEX IF NOT EXISTS idx_exceptions_comp_rule ON Exceptions(CompanyId, RuleId);
            CREATE INDEX IF NOT EXISTS idx_exceptions_status ON Exceptions(CompanyId, Status);
        ";

        await connection.ExecuteAsync(new CommandDefinition(schemaSql, cancellationToken: cancellationToken));
        _logger.LogInformation("Database tables and indexes verified successfully.");
    }

    private async Task SeedInitialRulesAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        const string seedSql = @"
            INSERT OR IGNORE INTO AuditRules (RuleId, Category, Name, Description, Severity, SuggestedReview, Version, IsEnabled)
            VALUES 
            ('GST-001', 1, 'Missing GSTIN on High-Value B2B Purchase', 'Vouchers categorized as inter-state or B2B purchase where party ledger has no GSTIN recorded.', 3, 'Verify vendor registration status on GST portal and update ledger master if registered.', '1.0.0', 1),
            ('GST-002', 1, 'Inter-state IGST charged on Intra-state Supply', 'IGST appears on a voucher where the party state matches company state.', 4, 'Verify Place of Supply and determine if CGST+SGST was applicable instead.', '1.0.0', 1),
            ('GST-003', 1, 'Ineligible ITC on Personal or Motor Vehicle Expense', 'Voucher contains ITC claim on blocked credits under Section 17(5).', 3, 'Inspect tax ledger allocation and reverse ITC if not eligible for business purposes.', '1.0.0', 1),
            ('TDS-001', 2, 'Threshold Exceeded Without TDS Deduction', 'Contractor payment under Sec 194C exceeds single threshold Rs.30,000 or aggregate Rs.1,00,000 without corresponding TDS entry.', 3, 'Check if lower deduction certificate (Form 13) or non-deduction declaration exists.', '1.0.0', 1),
            ('TDS-002', 2, 'Missing PAN Higher Rate Deduction Check', 'Party with TDS liability does not have a valid PAN recorded (triggers Sec 206AA 20% rate).', 4, 'Obtain PAN from vendor or verify why 20% higher deduction rate was not charged.', '1.0.0', 1),
            ('ACC-001', 0, 'Negative Cash Balance on Transaction Date', 'Daily cumulative cash ledger balance falls below zero at end of transaction day.', 4, 'Check for unrecorded cash receipts or backdated payment vouchers.', '1.0.0', 1),
            ('ACC-002', 0, 'Direct Entry in Suspense Ledger', 'Vouchers posted directly to Suspense or Rounding Off accounts exceeding normal variance.', 2, 'Reclassify suspense entries to appropriate vendor or expense heads.', '1.0.0', 1),
            ('DUP-001', 6, 'Duplicate Supplier Bill Reference', 'Identical reference invoice number recorded more than once for the same supplier.', 3, 'Cross-check against vendor statement to ensure invoice is not double-booked.', '1.0.0', 1),
            ('ANO-001', 5, 'Unusual High-Value Round-Number Payment', 'Cash or bank disbursements in exact multiples of Rs.10,000 exceeding Rs.50,000.', 1, 'Review supporting vouchers and internal payment authorization.', '1.0.0', 1);

            INSERT OR IGNORE INTO Settings (Key, Value) VALUES ('TallyHost', 'localhost');
            INSERT OR IGNORE INTO Settings (Key, Value) VALUES ('TallyPort', '9000');
            INSERT OR IGNORE INTO Settings (Key, Value) VALUES ('AutoSyncIntervalMinutes', '60');
            INSERT OR IGNORE INTO Settings (Key, Value) VALUES ('IsMockModeEnabled', 'false');
        ";

        await connection.ExecuteAsync(new CommandDefinition(seedSql, cancellationToken: cancellationToken));
    }
}
