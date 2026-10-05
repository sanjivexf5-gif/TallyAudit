using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.Core.Common;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine.Services;
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

        _service = new AuditTrailService(
            _repository,
            NullLogger<AuditTrailService>.Instance,
            _mockCompanyContext.Object);
    }

    public Task InitializeAsync() => _initializer.InitializeAsync();

    public async Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_testDbPath))
                File.Delete(_testDbPath);
        }
        catch { }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task RecordActivityAsync_PersistsRequiredFields()
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
        var entry = Assert.Single(entries);

        Assert.StartsWith("LOG-", entry.Id);
        Assert.Equal("Company selected", entry.ActionType);
        Assert.Equal("WORKSPACE", entry.Module);
        Assert.Equal("Test Corp Ltd", entry.CompanyName);
        Assert.Equal("FY 2025-26", entry.FinancialYear);
        Assert.Equal("Company", entry.EntityType);
        Assert.Equal("COMP-001", entry.EntityId);
        Assert.Equal("Test Corp Ltd", entry.NewState);
        Assert.Equal("{\"source\":\"UI\"}", entry.Details);
        Assert.Equal(AppVersion.Version, entry.ApplicationVersion);
        Assert.Equal(Environment.MachineName, entry.MachineName);
        Assert.NotEmpty(entry.IntegrityHash);
    }

    [Fact]
    public async Task RecordActivityAsync_UsesActiveCompanyContextWhenNotProvided()
    {
        await _service.RecordActivityAsync(
            "Audit run started",
            "AUDIT",
            "Audit execution began");

        var entry = Assert.Single(await _repository.GetEntriesAsync(limit: 10));

        Assert.Equal("Test Corp Ltd", entry.CompanyName);
        Assert.Equal("FY 2025-26", entry.FinancialYear);
    }

    [Fact]
    public async Task SearchAndFilter_ReturnExpectedEntries()
    {
        await _service.RecordActivityAsync("Synchronization started", "SYNC", "Voucher sync phase 1", companyName: "Alpha Ltd");
        await _service.RecordActivityAsync("Audit run completed", "AUDIT", "Found 5 TDS errors", companyName: "Alpha Ltd");
        await _service.RecordActivityAsync("Report exported", "REPORTS", "Excel exported", companyName: "Beta LLP");

        var tds = await _service.GetEntriesAsync(searchTerm: "TDS");
        Assert.Single(tds);
        Assert.Equal("Audit run completed", tds[0].ActionType);

        var alpha = await _service.GetEntriesAsync(companyName: "Alpha Ltd");
        Assert.Equal(2, alpha.Count);

        var sync = await _service.GetEntriesAsync(module: "SYNC");
        Assert.Single(sync);
    }

    [Fact]
    public async Task DateFilter_ReturnsOnlyEntriesInRange()
    {
        var now = DateTime.UtcNow;

        await _repository.InsertAsync(new AuditTrailEntry
        {
            Id = "TEST-OLD",
            TimestampUtc = now.AddDays(-10),
            ActionType = "Sync",
            Module = "SYNC",
            Description = "Old sync",
            CompanyName = "Test Corp"
        });

        await _repository.InsertAsync(new AuditTrailEntry
        {
            Id = "TEST-NEW",
            TimestampUtc = now,
            ActionType = "Sync",
            Module = "SYNC",
            Description = "Recent sync",
            CompanyName = "Test Corp"
        });

        var recent = await _service.GetEntriesAsync(fromUtc: now.AddDays(-2));
        var entry = Assert.Single(recent);

        Assert.Equal("TEST-NEW", entry.Id);
    }

    [Fact]
    public async Task StateTransition_PreservesPreviousAndNewState()
    {
        await _service.RecordActivityAsync(
            "Finding resolved",
            "EXCEPTIONS",
            "Resolved finding",
            entityType: "AuditException",
            entityId: "EXC-101",
            previousState: "Investigating",
            newState: "Resolved",
            companyName: "Test Corp Ltd");

        var entry = Assert.Single(await _service.GetEntriesAsync(companyName: "Test Corp Ltd"));

        Assert.Equal("Investigating", entry.PreviousState);
        Assert.Equal("Resolved", entry.NewState);
    }

    [Fact]
    public async Task Security_SanitizesPasswordsAndTokens()
    {
        await _service.RecordActivityAsync(
            "Settings changed",
            "SETTINGS",
            "Updated password: MySecretPassword123 and token=abcxyz987token",
            details: "apikey: SecretApiKey12345");

        var entry = Assert.Single(await _service.GetEntriesAsync());

        Assert.DoesNotContain("MySecretPassword123", entry.Description);
        Assert.DoesNotContain("abcxyz987token", entry.Description);
        Assert.DoesNotContain("SecretApiKey12345", entry.Details);
        Assert.Contains("[REDACTED]", entry.Description);
        Assert.Contains("[REDACTED]", entry.Details);
    }

    [Fact]
    public async Task ExportToExcelAndPdf_ReturnsExpectedPayloads()
    {
        await _service.RecordActivityAsync("Synchronization completed", "SYNC", "Synced 100 vouchers", companyName: "Test Corp");
        await _service.RecordActivityAsync("Audit run completed", "AUDIT", "Found 2 issues", companyName: "Test Corp");

        var entries = await _service.GetEntriesAsync();
        var excelBytes = await _service.ExportToExcelAsync(entries);
        var pdfBytes = await _service.ExportToPdfAsync(entries);

        Assert.NotEmpty(excelBytes);
        Assert.NotEmpty(pdfBytes);

        var excelText = Encoding.UTF8.GetString(excelBytes);
        var pdfText = Encoding.UTF8.GetString(pdfBytes);

        Assert.Contains("Synchronization completed", excelText);
        Assert.Contains("Audit run completed", excelText);
        Assert.Contains("TALLY AUDIT ASSISTANT — STATUTORY AUDIT TRAIL", pdfText);
        Assert.Contains("Synced 100 vouchers", pdfText);
    }

    [Fact]
    public async Task PersistenceFailure_DoesNotEscapeAuditService()
    {
        var faultyRepo = new Mock<IAuditTrailRepository>();
        faultyRepo
            .Setup(r => r.InsertAsync(It.IsAny<AuditTrailEntry>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated SQLite failure"));

        var safeService = new AuditTrailService(
            faultyRepo.Object,
            NullLogger<AuditTrailService>.Instance,
            _mockCompanyContext.Object);

        var exception = await Record.ExceptionAsync(() =>
            safeService.RecordActivityAsync("Sync", "SYNC", "Test"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task AuditTrail_SurvivesNewRepositoryInstance()
    {
        await _service.RecordActivityAsync(
            "Backup created",
            "BACKUP",
            "Created automated backup",
            companyName: "Test Corp");

        var newRepository = new AuditTrailRepository(
            _factory,
            NullLogger<AuditTrailRepository>.Instance);

        var entries = await newRepository.GetEntriesAsync();

        var entry = Assert.Single(entries);
        Assert.Equal("Backup created", entry.ActionType);
        Assert.Equal("Created automated backup", entry.Description);
    }
}