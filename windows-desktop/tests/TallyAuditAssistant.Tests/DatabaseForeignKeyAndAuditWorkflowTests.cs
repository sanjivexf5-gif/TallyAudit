using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Vouchers;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine;
using TallyAuditAssistant.Engine.Rules;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class DatabaseForeignKeyAndAuditWorkflowTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly AuditRepository _auditRepo;
    private readonly AuditRuleRepository _ruleRepo;
    private readonly AuditResultRepository _resultRepo;
    private readonly SyncRepository _syncRepo;

    public DatabaseForeignKeyAndAuditWorkflowTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"fk_audit_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance, _testDbPath);
        _auditRepo = new AuditRepository(_factory);
        _ruleRepo = new AuditRuleRepository(_factory, NullLogger<AuditRuleRepository>.Instance);
        _resultRepo = new AuditResultRepository(_factory, NullLogger<AuditResultRepository>.Instance);
        _syncRepo = new SyncRepository(_factory, NullLogger<SyncRepository>.Instance);
    }

    public async Task InitializeAsync()
    {
        await _initializer.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
        }
        catch { }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task AuditEngine_Execution_PersistsFindings_WithValidForeignKeys()
    {
        // 1. Setup Company
        var company = new Company
        {
            Id = "Demo Industrial Solutions Pvt Ltd (FY 2025-26)",
            TallyCompanyName = "Demo Industrial Solutions Pvt Ltd (FY 2025-26)",
            FormalName = "Demo Industrial Solutions Pvt Ltd",
            GSTIN = "27DEMO1234F1Z9",
            PAN = "DEMOP1234F",
            StateName = "Maharashtra",
            BooksFromDate = new DateTime(2025, 4, 1),
            LastSyncDate = DateTime.UtcNow,
            IsActive = true
        };
        await _auditRepo.EnsureCompanyAsync(company);

        // 2. Setup Vouchers & Entries
        var vouchers = new List<Voucher>
        {
            new Voucher
            {
                Id = "VOUCH-FK-001",
                CompanyId = company.Id,
                VoucherTypeId = "VT-SALES",
                VoucherTypeName = "Sales",
                VoucherNumber = "SAL-101",
                VoucherDate = new DateTime(2025, 5, 15),
                TotalAmount = 150000m,
                PartyLedgerName = "Acme Traders",
                AlterId = 101
            },
            new Voucher
            {
                Id = "VOUCH-FK-002",
                CompanyId = company.Id,
                VoucherTypeId = "VT-JOURNAL",
                VoucherTypeName = "Journal",
                VoucherNumber = "JRN-201",
                VoucherDate = new DateTime(2025, 6, 20),
                TotalAmount = 50000m,
                Narration = "", // Empty narration triggers ACC-NAR-01 rule
                AlterId = 102
            }
        };
        await _syncRepo.BatchUpsertVouchersAsync(vouchers, company.Id);

        // 3. Register All Production Rules into AuditEngine
        var rules = new List<IAuditRule>
        {
            new DuplicateVoucherRule(_factory),
            new DuplicateInvoiceNumberRule(_factory),
            new MissingNarrationRule(_factory),
            new MissingPartyInfoRule(_factory),
            new NegativeLedgerBalanceRule(_factory),
            new SuspenseLedgerActivityRule(_factory),
            new UnusualJournalEntryRule(_factory),
            new LargeManualJournalRule(_factory),
            new BackdatedTransactionRule(_factory),
            new YearEndAdjustmentRule(_factory),
            new VoucherNumberingGapRule(_factory),
            new UnusualTransactionAmountRule(_factory),
            new RoundNumberPatternRule(_factory),
            new UnusualLedgerCombinationRule(_factory),
            new ReversalAnomalyRule(_factory),
            new CreditDebitNoteAnomalyRule(_factory),
            new MissingGstInformationRule(_factory),
            new MissingPanWhereApplicableRule(_factory),
            new MissingHsnSacRule(_factory),
            new GstTaxCalculationConsistencyRule(_factory),
            new InputTaxCreditReviewRule(_factory),
            new OutputGstReviewRule(_factory),
            new TdsApplicabilityThresholdRule(_factory),
            new LargeTransactionRule(_factory),
            new PeriodEndTransactionReviewRule(_factory),
            new MasterDataQualityCheckRule(_factory),
            new CrossDatasetConsistencyCheckRule(_factory)
        };

        var engine = new AuditEngine(_ruleRepo, _resultRepo, NullLogger<AuditEngine>.Instance, rules);

        // 4. Create Audit Run Record
        var run = new AuditRun
        {
            CompanyId = company.Id,
            Period = "01-Apr-2025 to 31-Mar-2026",
            StartTime = DateTime.UtcNow,
            TransactionsAnalysed = 2,
            Status = "Running"
        };
        await _auditRepo.SaveAuditRunAsync(run);

        // 5. Execute Audit
        var context = new AuditExecutionContext(company.Id, new DateTime(2025, 4, 1), new DateTime(2026, 3, 31));
        var results = await engine.ExecuteAuditAsync(context);

        // Update Audit Run Status
        run.EndTime = DateTime.UtcNow;
        run.FindingsGenerated = results.Count;
        run.Status = "Completed";
        await _auditRepo.SaveAuditRunAsync(run);

        // 6. Verify Foreign Key PRAGMA check on SQLite database
        using var conn = await _factory.CreateConnectionAsync();
        var fkViolations = (await conn.QueryAsync("PRAGMA foreign_key_check;")).ToList();
        Assert.Empty(fkViolations);

        // 7. Verify persisted exceptions & catalog alignment
        var persistedExceptions = await _resultRepo.GetResultsAsync(company.Id);
        Assert.Equal(results.Count, persistedExceptions.Count);

        if (persistedExceptions.Count > 0)
        {
            var ruleIdsInExceptions = persistedExceptions.Select(e => e.RuleId).Distinct().ToList();
            var dbRules = (await _ruleRepo.GetAllRulesAsync()).Select(r => r.RuleId).ToHashSet();

            foreach (var rId in ruleIdsInExceptions)
            {
                Assert.Contains(rId, dbRules);
            }

            var companyIdsInExceptions = persistedExceptions.Select(e => e.CompanyId).Distinct().ToList();
            var dbCompanies = (await _auditRepo.GetAllCompaniesAsync()).Select(c => c.Id).ToHashSet();

            foreach (var cId in companyIdsInExceptions)
            {
                Assert.Contains(cId, dbCompanies);
            }
        }

        // 8. Verify AuditRun reached Completed
        var runHistory = await _auditRepo.GetAuditRunsAsync(company.Id);
        Assert.NotEmpty(runHistory);
        Assert.Equal("Completed", runHistory[0].Status);
    }

    [Fact]
    public async Task InvalidRuleId_RejectsException_WhenForeignKeyEnforced()
    {
        using var conn = await _factory.CreateConnectionAsync();

        var invalidResult = new AuditResult
        {
            ResultId = Guid.NewGuid().ToString(),
            CompanyId = "NON-EXISTENT-COMPANY",
            RuleId = "INVALID-NON-EXISTENT-RULE-ID",
            RuleName = "Fake Test Rule",
            Category = RuleCategory.GeneralAccounting,
            Severity = SeverityLevel.High,
            Explanation = "Test exception"
        };

        // When inserting directly without cataloging the rule, SQLite foreign key enforcement triggers error
        const string insertSql = @"
            INSERT INTO Exceptions (
                Id, CompanyId, RuleId, RuleName, Category, Severity, Explanation, EvidenceJson
            )
            VALUES (
                @ResultId, @CompanyId, @RuleId, @RuleName, 0, 3, @Explanation, '{}'
            );
        ";

        await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            await conn.ExecuteAsync(new CommandDefinition(insertSql, invalidResult));
        });
    }
}
