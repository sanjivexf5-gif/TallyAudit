using Dapper;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data;

public class DatabaseInitializer : IDatabaseInitializer
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly string _databasePath;

    static DatabaseInitializer()
    {
        SqlMapper.AddTypeHandler(SqliteDecimalHandler.Instance);
        SqlMapper.AddTypeHandler(SqliteNullableDecimalHandler.Instance);
    }

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
                IsMock INTEGER DEFAULT 0,
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
                TdsRate DECIMAL(5,2),
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
                DetectedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
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

            -- 7. Audit Runs History
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

            -- 8. Tally Corrections & Audit Logs
            CREATE TABLE IF NOT EXISTS TallyCorrections (
                Id TEXT PRIMARY KEY,
                CompanyId TEXT NOT NULL REFERENCES Companies(Id) ON DELETE CASCADE,
                FinancialPeriodId TEXT,
                AuditFindingId TEXT,
                VoucherId TEXT,
                VoucherNumber TEXT,
                LedgerId TEXT,
                LedgerName TEXT,
                CorrectionType INTEGER NOT NULL,
                FieldName TEXT NOT NULL,
                OriginalValue TEXT,
                ProposedValue TEXT,
                OriginalAmount DECIMAL(18,2),
                ProposedAmount DECIMAL(18,2),
                Reason TEXT NOT NULL,
                EvidenceId TEXT,
                Status INTEGER DEFAULT 0,
                CreatedBy TEXT NOT NULL,
                CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                ApprovedBy TEXT,
                ApprovedAt DATETIME,
                AppliedBy TEXT,
                AppliedAt DATETIME,
                TallyResponse TEXT,
                TallyTransactionReference TEXT,
                VerificationStatus TEXT,
                VerifiedAt DATETIME,
                FailureReason TEXT,
                BeforeSnapshot TEXT,
                AfterSnapshot TEXT,
                CorrelationId TEXT NOT NULL,
                IsAiAssisted INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS TallyCorrectionAuditLogs (
                Id TEXT PRIMARY KEY,
                CorrectionId TEXT NOT NULL REFERENCES TallyCorrections(Id) ON DELETE CASCADE,
                CompanyId TEXT NOT NULL,
                Action TEXT NOT NULL,
                Actor TEXT NOT NULL,
                Details TEXT NOT NULL,
                CorrelationId TEXT NOT NULL,
                Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
            );

            -- 10. Audit Finalization & Workflow
            CREATE TABLE IF NOT EXISTS AuditFinalizationStates (
                Id TEXT PRIMARY KEY,
                CompanyId TEXT NOT NULL REFERENCES Companies(Id) ON DELETE CASCADE,
                FinancialPeriodId TEXT NOT NULL,
                Status INTEGER NOT NULL,
                CompletionPercentage REAL NOT NULL,
                AuditorConclusionStatus TEXT NOT NULL,
                AuditorConclusionText TEXT,
                AuditorConclusionBasis TEXT,
                AuditorConclusionDate DATETIME,
                AuditorConclusionPreparedBy TEXT,
                ReviewerName TEXT,
                ReviewerComments TEXT,
                ReviewedAt DATETIME,
                FinalizedBy TEXT,
                FinalizedAt DATETIME
            );

            CREATE TABLE IF NOT EXISTS AuditChecklistItems (
                Id TEXT PRIMARY KEY,
                AuditId TEXT NOT NULL REFERENCES AuditFinalizationStates(Id) ON DELETE CASCADE,
                Section TEXT NOT NULL,
                Code TEXT NOT NULL,
                Description TEXT NOT NULL,
                IsCompleted INTEGER DEFAULT 0,
                CompletedAt DATETIME,
                CompletedBy TEXT,
                Notes TEXT,
                SourceReference TEXT
            );

            CREATE TABLE IF NOT EXISTS AuditOpenItems (
                Id TEXT PRIMARY KEY,
                AuditId TEXT NOT NULL REFERENCES AuditFinalizationStates(Id) ON DELETE CASCADE,
                Description TEXT NOT NULL,
                Category TEXT NOT NULL,
                Priority TEXT NOT NULL,
                Owner TEXT NOT NULL,
                DueDate DATETIME,
                Status TEXT NOT NULL,
                RelatedFindingId TEXT,
                RelatedProcedureId TEXT,
                RelatedEvidenceId TEXT,
                Remarks TEXT
            );

            CREATE TABLE IF NOT EXISTS AuditReviewNotes (
                Id TEXT PRIMARY KEY,
                AuditId TEXT NOT NULL REFERENCES AuditFinalizationStates(Id) ON DELETE CASCADE,
                Area TEXT NOT NULL,
                Reference TEXT NOT NULL,
                Reviewer TEXT NOT NULL,
                CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                Comment TEXT NOT NULL,
                Status TEXT NOT NULL,
                ResolvedBy TEXT,
                ResolvedAt DATETIME,
                Resolution TEXT
            );

            CREATE TABLE IF NOT EXISTS AuditAmendments (
                Id TEXT PRIMARY KEY,
                AuditId TEXT NOT NULL REFERENCES AuditFinalizationStates(Id) ON DELETE CASCADE,
                Reason TEXT NOT NULL,
                RequestedBy TEXT NOT NULL,
                RequestedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                ApprovedBy TEXT,
                ApprovedAt DATETIME,
                Status TEXT NOT NULL,
                Description TEXT
            );

            -- 9. Performance Indexes
            CREATE INDEX IF NOT EXISTS idx_vouchers_comp_date ON Vouchers(CompanyId, VoucherDate);
            CREATE INDEX IF NOT EXISTS idx_vouchers_comp_type ON Vouchers(CompanyId, VoucherTypeName);
            CREATE INDEX IF NOT EXISTS idx_vouchers_alterid ON Vouchers(CompanyId, AlterId);
            CREATE INDEX IF NOT EXISTS idx_entries_voucher ON VoucherEntries(VoucherId);
            CREATE INDEX IF NOT EXISTS idx_entries_ledger ON VoucherEntries(LedgerName);
            CREATE INDEX IF NOT EXISTS idx_exceptions_comp_rule ON Exceptions(CompanyId, RuleId);
            CREATE INDEX IF NOT EXISTS idx_exceptions_status ON Exceptions(CompanyId, Status);
            CREATE INDEX IF NOT EXISTS idx_auditruns_comp ON AuditRuns(CompanyId);
            CREATE INDEX IF NOT EXISTS idx_corrections_comp_status ON TallyCorrections(CompanyId, Status);
            CREATE INDEX IF NOT EXISTS idx_finalization_states ON AuditFinalizationStates(CompanyId, Status);
        ";

        await connection.ExecuteAsync(new CommandDefinition(schemaSql, cancellationToken: cancellationToken));

        try
        {
            await connection.ExecuteAsync(new CommandDefinition("ALTER TABLE Companies ADD COLUMN IsMock INTEGER DEFAULT 0;", cancellationToken: cancellationToken));
        }
        catch
        {
            // Column already present
        }

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(@"
                UPDATE Companies 
                SET IsMock = 1 
                WHERE TallyCompanyName LIKE '%Demo Industrial%' 
                   OR TallyCompanyName LIKE '%Apex Industrial%' 
                   OR TallyCompanyName LIKE '%Delta Retail%';
            ", cancellationToken: cancellationToken));
        }
        catch
        {
            // Ignore if already configured
        }

        _logger.LogInformation("Database tables and indexes verified successfully.");
    }

    private async Task SeedInitialRulesAsync(CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        const string seedSql = @"
            INSERT OR IGNORE INTO AuditRules (RuleId, Category, Name, Description, Severity, SuggestedReview, Version, IsEnabled)
            VALUES 
            ('ACC-DUP-01', 6, 'Duplicate Voucher Number', 'Voucher number repeated across multiple entries for same type.', 3, 'Inspect duplicate voucher references.', '1.0.0', 1),
            ('ACC-DUP-02', 6, 'Duplicate Supplier Invoice Number', 'Identical supplier bill reference recorded more than once.', 3, 'Check vendor statement for double booking.', '1.0.0', 1),
            ('ACC-JRN-01', 0, 'Unusual Journal Entry', 'Journal voucher posted directly to cash/bank or unexpected heads.', 2, 'Verify journal authorization.', '1.0.0', 1),
            ('ACC-JRN-02', 0, 'Large Manual Journal Entry', 'High value journal voucher exceeding standard thresholds.', 3, 'Inspect supporting working paper.', '1.0.0', 1),
            ('ACC-TIM-01', 0, 'Backdated Transaction Entry', 'Voucher date significantly precedes system entry timestamp.', 2, 'Review voucher entry audit log.', '1.0.0', 1),
            ('ACC-TIM-02', 0, 'Year-End Period Adjustment', 'High volume adjustments posted in final days of financial year.', 3, 'Verify period cutoff controls.', '1.0.0', 1),
            ('ACC-TIM-03', 0, 'Period End Transaction Review', 'Unusually high transaction volume at month/year end.', 2, 'Review cutoff and accruals.', '1.0.0', 1),
            ('ACC-SEQ-01', 0, 'Voucher Numbering Sequence Gap', 'Missing sequential number in numerical voucher series.', 2, 'Verify whether missing voucher was deleted or omitted.', '1.0.0', 1),
            ('ACC-ANO-01', 0, 'Unusual High Transaction Amount', 'Voucher amount exceeds historical statistical mean by multiple std devs.', 2, 'Verify supporting documentation.', '1.0.0', 1),
            ('ACC-ANO-02', 0, 'Round Number Amount Pattern', 'Frequent round sum disbursements exceeding threshold.', 1, 'Review authorization for cash/bank payments.', '1.0.0', 1),
            ('ACC-ANO-03', 0, 'Large Transaction Audit', 'High-value transaction exceeding materiality threshold.', 3, 'Perform detailed voucher audit.', '1.0.0', 1),
            ('ACC-LGD-01', 0, 'Unusual Ledger Combination', 'Debit/Credit posting between unrelated accounting heads.', 2, 'Verify account classification.', '1.0.0', 1),
            ('ACC-REV-01', 0, 'Transaction Reversal Anomaly', 'Immediate debit/credit reversal without explanation.', 2, 'Verify reversal rationale.', '1.0.0', 1),
            ('ACC-CDN-01', 0, 'Credit/Debit Note Anomaly', 'Note issued without original invoice reference.', 3, 'Match against original tax invoice.', '1.0.0', 1),
            ('ACC-NAR-01', 0, 'Missing or Minimal Narration', 'Voucher contains blank or generic narration text.', 1, 'Add descriptive transaction narration.', '1.0.0', 1),
            ('ACC-PTY-01', 0, 'Missing Party Information', 'Trade voucher missing party ledger details.', 2, 'Update party ledger allocation.', '1.0.0', 1),
            ('ACC-BAL-01', 0, 'Negative Ledger Balance', 'Cash or asset balance negative on transaction date.', 4, 'Check for unrecorded receipts or backdated entries.', '1.0.0', 1),
            ('ACC-SUS-01', 0, 'Direct Entry in Suspense Ledger', 'Direct posting to Suspense or Rounding Off ledger.', 2, 'Reclassify suspense entries.', '1.0.0', 1),
            ('ACC-CRS-01', 0, 'Cross Dataset Consistency Check', 'Discrepancy between general ledger and statutory summaries.', 3, 'Reconcile ledger vs return summary.', '1.0.0', 1),
            ('GST-MISS-01', 1, 'Missing GST Information', 'Trade voucher missing GSTIN or tax classification.', 3, 'Update GST master details.', '1.0.0', 1),
            ('GST-HSN-01', 1, 'Missing HSN/SAC Code', 'Invoice missing HSN or SAC classification code.', 2, 'Update item/ledger HSN master.', '1.0.0', 1),
            ('GST-CON-01', 1, 'GST Tax Calculation Inconsistency', 'Calculated tax amount deviates from applied rate.', 3, 'Recalculate GST taxable base and tax.', '1.0.0', 1),
            ('GST-ITC-01', 1, 'Input Tax Credit Ineligibility Review', 'ITC claimed on blocked credit or non-business expense.', 3, 'Verify Section 17(5) eligibility.', '1.0.0', 1),
            ('GST-OUT-01', 1, 'Output GST Rate Review', 'Output tax rate mismatch against statutory rate schedule.', 3, 'Verify Place of Supply and GST rate.', '1.0.0', 1),
            ('TDS-PAN-01', 2, 'Missing PAN for TDS Party', 'Deductee missing PAN triggering 20% higher rate under Sec 206AA.', 4, 'Obtain valid PAN or apply 20% rate.', '1.0.0', 1),
            ('TDS-THR-01', 2, 'TDS Applicability Threshold Exceeded', 'Payment exceeds threshold without TDS deduction.', 3, 'Check Form 13 or lower deduction certificate.', '1.0.0', 1),
            ('MST-QLY-01', 4, 'Master Data Quality Check', 'Master record missing statutory identifiers.', 2, 'Update master registration data.', '1.0.0', 1),
            ('REC-LGD-01', 3, 'Trial Balance Ledger Reconciliation', 'Ledger balance discrepancy across financial periods.', 3, 'Reconcile opening/closing ledger balances.', '1.0.0', 1),
            ('REC-VOU-01', 3, 'Ledger Voucher Reconciliation', 'Voucher posting sum does not equal ledger balance.', 3, 'Audit voucher postings.', '1.0.0', 1),
            ('REC-GST-01', 1, 'GST Rate Reconciliation', 'Tax posted does not match GSTR rate breakdown.', 3, 'Reconcile GST ledgers.', '1.0.0', 1),
            ('REC-GST-02', 1, 'GST Net Input/Output Reconciliation', 'Net ITC vs Output Liability anomaly.', 3, 'Reconcile GSTR-3B vs 2B.', '1.0.0', 1),
            ('REC-TDS-01', 2, 'TDS Expense Verification', 'Expense head total vs TDS deduction base discrepancy.', 3, 'Reconcile Form 26Q vs P&L expense.', '1.0.0', 1),
            ('REC-PTY-01', 3, 'Party Master Reconciliation', 'Party ledger GSTIN/PAN mismatch.', 2, 'Synchronize party master data.', '1.0.0', 1),
            ('REC-SLS-01', 1, 'Sales GST Reconciliation', 'Sales turnover vs GST turnover discrepancy.', 3, 'Reconcile P&L sales with GSTR-1.', '1.0.0', 1),
            ('REC-PUR-01', 1, 'Purchase GST Reconciliation', 'Purchase booking vs GSTR-2B discrepancy.', 3, 'Reconcile purchase register with GSTR-2B.', '1.0.0', 1),
            ('REC-EXP-01', 2, 'Expense TDS Reconciliation', 'P&L expense total vs 26Q TDS base mismatch.', 3, 'Reconcile expense accounts.', '1.0.0', 1),
            ('REC-CSH-01', 0, 'Bank Cash Reconciliation', 'Bank statement vs Cash book discrepancies.', 3, 'Reconcile bank statement.', '1.0.0', 1),
            ('REC-CON-01', 0, 'Contra Verification Check', 'Inter-account cash/bank contra mismatch.', 2, 'Verify contra entries.', '1.0.0', 1),
            ('REC-PRD-01', 0, 'Period Cutoff Reconciliation', 'Transactions recorded outside accounting period.', 3, 'Check period cutoff.', '1.0.0', 1),
            ('GST-001', 1, 'Missing GSTIN on High-Value B2B Purchase', 'Vouchers categorized as inter-state or B2B purchase where party ledger has no GSTIN recorded.', 3, 'Verify vendor registration status on GST portal and update ledger master if registered.', '1.0.0', 1),
            ('GST-002', 1, 'Inter-state IGST charged on Intra-state Supply', 'IGST appears on a voucher where the party state matches company state.', 4, 'Verify Place of Supply and determine if CGST+SGST was applicable instead.', '1.0.0', 1),
            ('GST-003', 1, 'Ineligible ITC on Personal or Motor Vehicle Expense', 'Voucher contains ITC claim on blocked credits under Section 17(5).', 3, 'Inspect tax ledger allocation and reverse ITC if not eligible for business purposes.', '1.0.0', 1),
            ('TDS-001', 2, 'Threshold Exceeded Without TDS Deduction', 'Contractor payment under Sec 194C exceeds single threshold Rs.30,000 or aggregate Rs.1,00,000 without corresponding TDS entry.', 3, 'Check if lower deduction certificate (Form 13) or non-deduction declaration exists.', '1.0.0', 1),
            ('TDS-002', 2, 'Missing PAN Higher Rate Deduction Check', 'Party with TDS liability does not have a valid PAN recorded (triggers Sec 206AA 20% rate).', 4, 'Obtain PAN from vendor or verify why 20% higher deduction rate was not charged.', '1.0.0', 1),
            ('TDS-CHK-01', 2, 'Potential Statutory TDS Applicability', 'Identifies inward commercial/professional heads.', 3, 'Review contract terms.', '1.0.0', 1),
            ('TDS-CHK-02', 2, 'Single Transaction Threshold', 'Monitors single-bill thresholds.', 3, 'Review invoice withholding.', '1.0.0', 1),
            ('TDS-CHK-03', 2, 'Deductee PAN Availability', 'Verifies 10-char PAN.', 4, 'Check Section 206AA.', '1.0.0', 1),
            ('TDS-CHK-04', 2, 'TDS Chart Mapping Check', 'Ensures Duties & Taxes parenting.', 2, 'Reclassify chart hierarchy.', '1.0.0', 1),
            ('TDS-CHK-05', 2, 'TDS Deduction Math Check', 'Recalculates rate * base value.', 3, 'Verify posted deduction.', '1.0.0', 1),
            ('TDS-CHK-06', 2, 'TDS Section Classification', 'Cross-checks expense vs TDS head.', 3, 'Review 194C vs 194J.', '1.0.0', 1),
            ('TDS-CHK-07', 2, 'Payee Aggregate Threshold', 'Aggregates multi-voucher totals.', 3, 'Check aggregate limits.', '1.0.0', 1),
            ('TDS-CHK-08', 2, 'Expense Head Category Audit', 'Analyzes annual debit turnovers.', 2, 'Review disallowance risk.', '1.0.0', 1),
            ('TDS-CHK-09', 2, 'TDS Payable Remittance Check', 'Tracks credit accumulations.', 4, 'Check challan deposits.', '1.0.0', 1),
            ('TDS-CHK-10', 2, 'High-Value Commercial Bills', 'Flags high-value service bills.', 3, 'Check withholding entries.', '1.0.0', 1),
            ('TDS-CHK-11', 2, 'Unusual TDS Rate Pattern', 'Detects non-statutory rates.', 2, 'Verify applied rate.', '1.0.0', 1),
            ('TDS-CHK-12', 2, 'TDS Reversal Anomaly', 'Detects debit reversals.', 3, 'Check journal entries.', '1.0.0', 1),
            ('TDS-CHK-13', 2, 'Threshold Border Analysis', 'Detects clustered border invoices.', 2, 'Rule out invoice splitting.', '1.0.0', 1),
            ('ACC-001', 0, 'Negative Cash Balance on Transaction Date', 'Daily cumulative cash ledger balance falls below zero at end of transaction day.', 4, 'Check for unrecorded cash receipts or backdated payment vouchers.', '1.0.0', 1),
            ('ACC-002', 0, 'Direct Entry in Suspense Ledger', 'Vouchers posted directly to Suspense or Rounding Off accounts exceeding normal variance.', 2, 'Reclassify suspense entries to appropriate vendor or expense heads.', '1.0.0', 1),
            ('DUP-001', 6, 'Duplicate Supplier Bill Reference', 'Identical reference invoice number recorded more than once for the same supplier.', 3, 'Cross-check against vendor statement to ensure invoice is not double-booked.', '1.0.0', 1),
            ('DUP-ExactDuplicate', 6, 'Exact Duplicate Transaction', 'Identical voucher number, date, amount, and party ledger.', 4, 'Verify whether transaction was double-posted.', '1.0.0', 1),
            ('DUP-LikelyDuplicate', 6, 'Likely Duplicate Transaction', 'Similar voucher details recorded within close date proximity.', 3, 'Inspect supporting invoice documents.', '1.0.0', 1),
            ('DUP-PossibleDuplicate', 6, 'Possible Duplicate Transaction', 'Matching amount and party with potential narrative variance.', 2, 'Review voucher entry audit trail.', '1.0.0', 1),
            ('ANO-001', 5, 'Unusual High-Value Round-Number Payment', 'Cash or bank disbursements in exact multiples of Rs.10,000 exceeding Rs.50,000.', 1, 'Review supporting vouchers and internal payment authorization.', '1.0.0', 1);

            INSERT OR IGNORE INTO Settings (Key, Value) VALUES ('TallyHost', 'localhost');
            INSERT OR IGNORE INTO Settings (Key, Value) VALUES ('TallyPort', '9000');
            INSERT OR IGNORE INTO Settings (Key, Value) VALUES ('AutoSyncIntervalMinutes', '60');
            INSERT OR IGNORE INTO Settings (Key, Value) VALUES ('IsMockModeEnabled', 'false');
        ";

        await connection.ExecuteAsync(new CommandDefinition(seedSql, cancellationToken: cancellationToken));
    }
}
