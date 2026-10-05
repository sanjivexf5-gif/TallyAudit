using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.Core.Common;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Sync;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Domain.Vouchers;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine;
using TallyAuditAssistant.Engine.Rules;
using TallyAuditAssistant.Engine.Services;
using TallyAuditAssistant.TallyIntegration;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class AuditTrailTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly AuditTrailRepository _repository;
    private readonly Mock<IActiveCompanyContext> _mockCompanyContext;
    private readonly AuditTrailService _service;

    public AuditTrailTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"audittrail_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance, _testDbPath);
        _repository = new AuditTrailRepository(_factory, NullLogger<AuditTrailRepository>.Instance);

        _mockCompanyContext = new Mock<IActiveCompanyContext>();
        _mockCompanyContext.Setup(c => c.ActiveCompanyName).Returns("Test Corp Ltd");
        _mockCompanyContext.Setup(c => c.TallyCompanyName).Returns("Test Corp Ltd");
        _mockCompanyContext.Setup(c => c.ActiveFinancialYear).Returns("FY 2025-26");

        _service = new AuditTrailService(_repository, NullLogger<AuditTrailService>.Instance, _mockCompanyContext.Object);
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
    public async Task RecordActivityAsync_PersistsAllRequiredColumns()
    {
        await _service.RecordActivityAsync(
            actionType: "Company selected",
            module: "WORKSPACE",
            description: "Active company selected: 'Test Corp Ltd'.",
            entityType: "Company",
            entityId: "COMP-001",
            previousState: null,
            newState: "Test Corp Ltd",
            details: "{\"source\":\"UI\"}",
            companyName: "Test Corp Ltd",
            financialYear: "FY 2025-26");

        var entries = await _repository.GetEntriesAsync(limit: 10);
        Assert.Single(entries);

        var entry = entries[0];
        Assert.NotNull(entry.Id);
        Assert.StartsWith("LOG-", entry.Id);
        Assert.Equal("Company selected", entry.ActionType);
        Assert.Equal("WORKSPACE", entry.Module);
        Assert.Equal("Test Corp Ltd", entry.CompanyName);
        Assert.Equal("FY 2025-26", entry.FinancialYear);
        Assert.Equal("Company", entry.EntityType);
        Assert.Equal("COMP-001", entry.EntityId);
        Assert.Null(entry.PreviousState);
        Assert.Equal("Test Corp Ltd", entry.NewState);
        Assert.Equal("Active company selected: 'Test Corp Ltd'.", entry.Description);
        Assert.Equal("{\"source\":\"UI\"}", entry.Details);
        Assert.Equal(AppVersion.Version, entry.ApplicationVersion);
        Assert.Equal(Environment.MachineName, entry.MachineName);
        Assert.Equal(Environment.UserName, entry.UserName);
        Assert.NotEmpty(entry.IntegrityHash);
    }

    [Fact]
    public async Task ContextFallback_UsesActiveCompanyContext_WhenNotExplicitlyProvided()
    {
        await _service.RecordActivityAsync(
            actionType: "Audit run started",
            module: "AUDIT",
            description: "Audit execution began");

        var entries = await _repository.GetEntriesAsync(limit: 10);
        Assert.Single(entries);
        Assert.Equal("Test Corp Ltd", entries[0].CompanyName);
        Assert.Equal("FY 2025-26", entries[0].FinancialYear);
    }

    [Fact]
    public async Task SearchAndFilter_BySearchTerm_FindsMatchingEntries()
    {
        await _service.RecordActivityAsync("Synchronization started", "SYNC", "Voucher sync phase 1", companyName: "Alpha Ltd");
        await _service.RecordActivityAsync("Audit run completed", "AUDIT", "Found 5 TDS errors", companyName: "Alpha Ltd");
        await _service.RecordActivityAsync("Report exported", "REPORTS", "Excel exported to file", companyName: "Beta LLP");

        var results = await _service.GetEntriesAsync(searchTerm: "TDS");
        Assert.Single(results);
        Assert.Equal("Audit run completed", results[0].ActionType);

        var alphaResults = await _service.GetEntriesAsync(searchTerm: "Alpha");
        Assert.Equal(2, alphaResults.Count);
    }

    [Fact]
    public async Task FilterByCompany_ReturnsOnlyTargetCompany()
    {
        await _service.RecordActivityAsync("Company selected", "WORKSPACE", "Selected A", companyName: "Company A");
        await _service.RecordActivityAsync("Synchronization completed", "SYNC", "Synced A", companyName: "Company A");
        await _service.RecordActivityAsync("Company selected", "WORKSPACE", "Selected B", companyName: "Company B");

        var compA = await _service.GetEntriesAsync(companyName: "Company A");
        Assert.Equal(2, compA.Count);
        Assert.All(compA, e => Assert.Equal("Company A", e.CompanyName));

        var compB = await _service.GetEntriesAsync(companyName: "Company B");
        Assert.Single(compB);
        Assert.Equal("Company B", compB[0].CompanyName);
    }

    [Fact]
    public async Task FilterByModuleAndAction_FiltersAccurately()
    {
        await _service.RecordActivityAsync("Synchronization started", "SYNC", "Starting sync", companyName: "Test Corp");
        await _service.RecordActivityAsync("Synchronization completed", "SYNC", "Finished sync", companyName: "Test Corp");
        await _service.RecordActivityAsync("Finding marked reviewed", "EXCEPTIONS", "Reviewed finding", companyName: "Test Corp");

        var syncEntries = await _service.GetEntriesAsync(module: "SYNC");
        Assert.Equal(2, syncEntries.Count);

        var reviewedEntries = await _service.GetEntriesAsync(actionType: "Finding marked reviewed");
        Assert.Single(reviewedEntries);
        Assert.Equal("EXCEPTIONS", reviewedEntries[0].Module);
    }

    [Fact]
    public async Task FilterByDateRange_FiltersAccurately()
    {
        var now = DateTime.UtcNow;
        var entry1 = new AuditTrailEntry
        {
            Id = "TEST-1",
            TimestampUtc = now.AddDays(-10),
            ActionType = "Sync",
            Module = "SYNC",
            Description = "Old sync",
            CompanyName = "Test Corp"
        };
        var entry2 = new AuditTrailEntry
        {
            Id = "TEST-2",
            TimestampUtc = now,
            ActionType = "Sync",
            Module = "SYNC",
            Description = "Recent sync",
            CompanyName = "Test Corp"
        };

        await _repository.InsertAsync(entry1);
        await _repository.InsertAsync(entry2);

        var recent = await _service.GetEntriesAsync(fromUtc: now.AddDays(-2));
        Assert.Single(recent);
        Assert.Equal("TEST-2", recent[0].Id);
    }

    [Fact]
    public async Task StateTransitions_RecordsPreviousAndNewStateCorrectly()
    {
        await _service.RecordActivityAsync(
            actionType: "Finding resolved",
            module: "EXCEPTIONS",
            description: "Resolved finding #EXC-101",
            entityType: "AuditException",
            entityId: "EXC-101",
            previousState: "Investigating",
            newState: "Resolved",
            companyName: "Test Corp Ltd");

        var entries = await _service.GetEntriesAsync(entityId: null);
        var entry = Assert.Single(entries);
        Assert.Equal("Investigating", entry.PreviousState);
        Assert.Equal("Resolved", entry.NewState);
    }

    [Fact]
    public async Task Security_SanitizesSensitivePasswordsAndTokens()
    {
        await _service.RecordActivityAsync(
            actionType: "Settings changed",
            module: "SETTINGS",
            description: "Updated settings with password: MySecretPassword123 and token=abcxyz987token",
            details: "apikey: SecretApiKey12345");

        var entries = await _service.GetEntriesAsync();
        var entry = Assert.Single(entries);

        Assert.DoesNotContain("MySecretPassword123", entry.Description);
        Assert.DoesNotContain("abcxyz987token", entry.Description);
        Assert.DoesNotContain("SecretApiKey12345", entry.Details);
        Assert.Contains("[REDACTED]", entry.Description);
        Assert.Contains("[REDACTED]", entry.Details);
    }

    [Fact]
    public async Task ExportToExcelAndPdf_GeneratesValidPayloads()
    {
        await _service.RecordActivityAsync("Synchronization completed", "SYNC", "Synced 100 vouchers", companyName: "Test Corp");
        await _service.RecordActivityAsync("Audit run completed", "AUDIT", "Found 2 issues", companyName: "Test Corp");

        var entries = await _service.GetEntriesAsync();

        var excelBytes = await _service.ExportToExcelAsync(entries);
        Assert.NotNull(excelBytes);
        Assert.True(excelBytes.Length > 0);
        var csvContent = Encoding.UTF8.GetString(excelBytes);
        Assert.Contains("Synchronization completed", csvContent);
        Assert.Contains("Audit run completed", csvContent);
        Assert.Contains("Test Corp", csvContent);

        var pdfBytes = await _service.ExportToPdfAsync(entries);
        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 0);
        var pdfContent = Encoding.UTF8.GetString(pdfBytes);
        Assert.Contains("TALLY AUDIT ASSISTANT — STATUTORY AUDIT TRAIL", pdfContent);
        Assert.Contains("Synced 100 vouchers", pdfContent);
    }

    [Fact]
    public async Task PersistenceFailure_DoesNotThrowExceptionToCaller()
    {
        var faultyRepoMock = new Mock<IAuditTrailRepository>();
        faultyRepoMock.Setup(r => r.InsertAsync(It.IsAny<AuditTrailEntry>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated SQLite disk lock failure"));

        var safeService = new AuditTrailService(faultyRepoMock.Object, NullLogger<AuditTrailService>.Instance, _mockCompanyContext.Object);

        // Should complete smoothly without bubbling exception
        var exception = await Record.ExceptionAsync(() => safeService.RecordActivityAsync("Sync", "SYNC", "Test"));
        Assert.Null(exception);
    }

    [Fact]
    public async Task AuditTrail_SurvivesApplicationRestart()
    {
        await _service.RecordActivityAsync("Backup created", "BACKUP", "Created automated backup", companyName: "Test Corp");

        // Simulate application restart by creating a new repository instance pointing to the same file
        var newRepo = new AuditTrailRepository(_factory, NullLogger<AuditTrailRepository>.Instance);
        var entries = await newRepo.GetEntriesAsync();

        Assert.Single(entries);
        Assert.Equal("Backup created", entries[0].ActionType);
        Assert.Equal("Created automated backup", entries[0].Description);
    }

    [Fact]
    public async Task IntegrationFlow_CompanySelection_Sync_AuditRun_FindingReview_ProducesAuditTrail()
    {
        var settingsRepo = new SettingsRepository(_factory, NullLogger<SettingsRepository>.Instance);
        var mockTallyService = new Mock<ITallyCompanyService>();
        var companyContext = new ActiveCompanyContext(new AuditRepository(_factory), settingsRepo, mockTallyService.Object, _service);

        // 1. Company Selection
        var compA = new Company { Id = "COMP-A", TallyCompanyName = "Company Alpha Pvt Ltd", BooksFromDate = new DateTime(2025, 4, 1) };
        await companyContext.SetActiveCompanyAsync(compA);

        var entriesAfterSelect = await _service.GetEntriesAsync(companyName: "Company Alpha Pvt Ltd");
        Assert.Contains(entriesAfterSelect, e => e.ActionType == "Company selected" && e.CompanyName == "Company Alpha Pvt Ltd");

        // 2. Synchronization
        var syncRepo = new SyncRepository(_factory, NullLogger<SyncRepository>.Instance);
        var auditRepo = new AuditRepository(_factory, _service);
        var mockConn = new Mock<ITallyConnection>();
        mockConn.Setup(c => c.TestConnectionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var mockMaster = new Mock<ITallyMasterService>();
        mockMaster.Setup(m => m.GetLedgersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<Ledger>());
        var mockVoucher = new Mock<ITallyVoucherService>();
        mockVoucher.Setup(v => v.GetVouchersStreamAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<Voucher>());

        mockTallyService.Setup(t => t.GetCompanyProfileTypedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompanyProfile { Name = "Company Alpha Pvt Ltd", BooksBeginningFrom = new DateTime(2025, 4, 1) });

        var syncManager = new SyncManager(
            mockConn.Object,
            mockTallyService.Object,
            mockMaster.Object,
            mockVoucher.Object,
            syncRepo,
            auditRepo,
            settingsRepo,
            NullLogger<SyncManager>.Instance,
            companyContext,
            _service);

        await syncManager.StartSyncAsync("Company Alpha Pvt Ltd", SyncMode.Full);

        var entriesAfterSync = await _service.GetEntriesAsync(companyName: "Company Alpha Pvt Ltd");
        Assert.Contains(entriesAfterSync, e => e.ActionType == "Synchronization started");
        Assert.Contains(entriesAfterSync, e => e.ActionType == "Synchronization completed");

        // 3. Audit Run
        var ruleRepo = new AuditRuleRepository(_factory, NullLogger<AuditRuleRepository>.Instance);
        var resultRepo = new AuditResultRepository(_factory, NullLogger<AuditResultRepository>.Instance, _service);
        var auditEngine = new AuditEngine(ruleRepo, resultRepo, NullLogger<AuditEngine>.Instance, auditTrailService: _service);

        var auditContext = new AuditExecutionContext(
            CompanyId: "Company Alpha Pvt Ltd",
            PeriodFrom: new DateTime(2025, 4, 1),
            PeriodTo: new DateTime(2026, 3, 31),
            Vouchers: new List<Voucher>(),
            Ledgers: new List<Ledger>());

        await auditEngine.ExecuteAuditAsync(auditContext);

        var entriesAfterAudit = await _service.GetEntriesAsync(companyName: "Company Alpha Pvt Ltd");
        Assert.Contains(entriesAfterAudit, e => e.ActionType == "Audit run started");
        Assert.Contains(entriesAfterAudit, e => e.ActionType == "Audit run completed");

        // 4. Change Company A to Company B
        var compB = new Company { Id = "COMP-B", TallyCompanyName = "Company Beta LLP", BooksFromDate = new DateTime(2025, 4, 1) };
        await companyContext.SetActiveCompanyAsync(compB);

        var entriesAfterChange = await _service.GetEntriesAsync(companyName: "Company Beta LLP");
        Assert.Contains(entriesAfterChange, e => e.ActionType == "Company changed" && e.CompanyName == "Company Beta LLP");
        Assert.Equal("Company Alpha Pvt Ltd", entriesAfterChange.First(e => e.ActionType == "Company changed").PreviousState);
    }

    [Fact]
    public async Task UpdateExceptionStatus_PersistsAndLogsValidReviewStatuses()
    {
        var auditRepo = new AuditRepository(_factory, _service);
        var resultRepo = new AuditResultRepository(_factory, NullLogger<AuditResultRepository>.Instance);

        var result = new AuditResult
        {
            ResultId = "RES-STATUS-101",
            CompanyId = "Company Alpha Pvt Ltd",
            RuleId = "GST-001",
            RuleName = "Missing GSTIN",
            Category = RuleCategory.GST,
            Severity = SeverityLevel.High,
            Explanation = "GSTIN missing",
            Status = ReviewStatus.Pending
        };

        await resultRepo.SaveResultsBatchAsync(new[] { result });

        // Update to Resolved (equivalent to accepted)
        await auditRepo.UpdateExceptionStatusAsync("RES-STATUS-101", ReviewStatus.Resolved, "Auditor accepted resolution");

        var exceptions = await auditRepo.GetExceptionsFilteredAsync("Company Alpha Pvt Ltd");
        var exc = Assert.Single(exceptions);
        Assert.Equal(ReviewStatus.Resolved, exc.Status);
        Assert.Equal("Auditor accepted resolution", exc.AuditorNote);

        var trail = await _service.GetEntriesAsync(companyName: "Company Alpha Pvt Ltd");
        Assert.Contains(trail, e => e.ActionType == "Finding resolved" && e.EntityId == "RES-STATUS-101");
    }
}
