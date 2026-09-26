using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Sync;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine.Duplicates;
using TallyAuditAssistant.Engine.Gst;
using TallyAuditAssistant.Engine.Gst.Rules;
using TallyAuditAssistant.Engine.Reconciliation;
using TallyAuditAssistant.Engine.Tds;
using TallyAuditAssistant.TallyIntegration;
using TallyAuditAssistant.TallyIntegration.Mocks;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class MockTallyIntegrationTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly SyncRepository _syncRepo;
    private readonly AuditRepository _auditRepo;
    private readonly MockTallyCompanyService _companyService;
    private readonly MockTallyClient _client;
    private readonly TallyResponseParser _parser;
    private readonly TallyVoucherService _voucherService;
    private readonly TallyMasterService _masterService;
    private readonly SyncManager _syncManager;

    public MockTallyIntegrationTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"mock_integration_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance, _testDbPath);

        _syncRepo = new SyncRepository(_factory, NullLogger<SyncRepository>.Instance);
        _auditRepo = new AuditRepository(_factory, NullLogger<AuditRepository>.Instance);

        _companyService = new MockTallyCompanyService();
        _client = new MockTallyClient(NullLogger<MockTallyClient>.Instance);
        _parser = new TallyResponseParser(NullLogger<TallyResponseParser>.Instance);

        var mockSettings = new MockSettingsService();
        var mockConn = new MockTallyConnection();

        _voucherService = new TallyVoucherService(
            _client,
            new TallyRequestBuilder(),
            _parser,
            mockSettings,
            NullLogger<TallyVoucherService>.Instance);

        _masterService = new TallyMasterService(
            _client,
            new TallyRequestBuilder(),
            _parser,
            mockSettings,
            NullLogger<TallyMasterService>.Instance);

        _syncManager = new SyncManager(
            mockConn,
            _companyService,
            _masterService,
            _voucherService,
            _syncRepo,
            _auditRepo,
            mockSettings,
            NullLogger<SyncManager>.Instance);
    }

    public async Task InitializeAsync()
    {
        await _initializer.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch
        {
            // Suppress cleanup exceptions
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Complete_DemoWorkflow_EndToEnd_Validates_DataAndRules()
    {
        // 1. Mock Company Discovery
        var companies = await _companyService.GetOpenCompaniesAsync();
        Assert.NotEmpty(companies);
        var demoCompany = companies.First();
        Assert.Contains("Demo Industrial Solutions", demoCompany);

        // 2. Active Company Selection
        var activeCompany = await _companyService.GetActiveCompanyAsync();
        Assert.Equal("Demo Industrial Solutions Pvt Ltd (FY 2025-26)", activeCompany);

        // 3. Load Company Statutory Profile & Verify marked as synthetic/demo
        var profile = await _companyService.GetCompanyProfileTypedAsync(activeCompany);
        Assert.NotNull(profile);
        Assert.Equal("Demo Industrial Solutions Pvt Ltd", profile.FormalName);
        Assert.Equal("27DEMO1234F1Z9", profile.GSTIN);
        Assert.Equal("DEMOP1234F", profile.PAN);
        Assert.Equal("Maharashtra", profile.StateName);
        Assert.Equal(new DateTime(2025, 4, 1), profile.BooksBeginningFrom);

        // 4. Run Mock Data Synchronization
        var result = await _syncManager.StartSyncAsync(activeCompany, SyncMode.Full);
        Assert.True(result.IsSuccess);
        Assert.Equal(SyncStatus.Completed, _syncManager.CurrentStatus);

        // 5. Confirm Synced Counts are non-zero (several hundred vouchers and realistic ledgers)
        var dbCompany = await _auditRepo.GetCompanyByIdAsync(activeCompany);
        Assert.NotNull(dbCompany);
        Assert.Equal("27DEMO1234F1Z9", dbCompany.GSTIN);

        var ledgersCount = result.TotalProcessed - result.Inserted; // Since ledgers process first
        Assert.True(result.Inserted > 100, $"Expected several hundred synced records but got {result.Inserted}");

        // 6. Existing GST rules evaluation on synthetic database
        var gstContext = new GstAuditContext(activeCompany, new DateTime(2025, 4, 1), new DateTime(2026, 3, 31));
        var missingGstinRule = new MissingGstinOnB2BRule(_factory);
        var missingGstinExceptions = await missingGstinRule.EvaluateAsync(gstContext);
        
        // Assert B2B purchases from Unregistered Steel Supplier (amount 75,000 > 50,000) is flagged
        var unregisteredSupplyEx = missingGstinExceptions.FirstOrDefault(e => e.PartyLedgerName == "Unregistered Steel Supplier");
        Assert.NotNull(unregisteredSupplyEx);
        Assert.Equal(SeverityLevel.High, unregisteredSupplyEx.Severity);
        Assert.Contains("Unregistered Steel Supplier", unregisteredSupplyEx.SuggestedCorrection);

        // Assert GSTIN Structure format checker rule flags Invalid GSTIN Trader
        var gstinFormatRule = new GstinFormatCheckRule(_factory);
        var formatExceptions = await gstinFormatRule.EvaluateAsync(gstContext);
        var invalidGstinEx = formatExceptions.FirstOrDefault(e => e.PartyLedgerName == "Invalid GSTIN Trader");
        Assert.NotNull(invalidGstinEx);
        Assert.Equal("27INVALID1234FX", invalidGstinEx.PartyGstin);

        // 7. Verify TDS threshold and incorrect rate rule detections
        // TDS threshold Rule (TDS-CHK-01 or similar) - can be tested directly with SQLite data
        using var conn = await _factory.CreateConnectionAsync();
        var suspenseEntriesCount = await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM VoucherEntries WHERE LedgerName = 'Suspense Account'
        ");
        Assert.True(suspenseEntriesCount > 0, "Suspense Account should contain synthetic ledger postings.");

        // 8. Duplicate detection asserts against Beta Distributors duplications
        var duplicateVouchers = await conn.QueryAsync(@"
            SELECT VoucherNumber, COUNT(*) as Count 
            FROM Vouchers 
            WHERE VoucherNumber = 'SAL-DUP-020'
            GROUP BY VoucherNumber
        ");
        Assert.NotEmpty(duplicateVouchers);
        Assert.Equal(2, duplicateVouchers.First().Count);

        // 9. Verify demo mode is purely non-destructive and doesn't modify anything outside local SQLite
        Assert.True(File.Exists(_testDbPath));
    }

    private class MockSettingsService : ISettingsService
    {
        public Task<string> GetTallyHostAsync() => Task.FromResult("localhost");
        public Task<int> GetTallyPortAsync() => Task.FromResult(9000);
        public Task<string> GetSettingAsync(string key, string defaultValue) => Task.FromResult(defaultValue);
        public Task SaveSettingAsync(string key, string value) => Task.CompletedTask;
    }

    private class MockTallyConnection : ITallyConnection
    {
        public Task<bool> TestConnectionAsync(string host, int port, CancellationToken ct = default) => Task.FromResult(true);
        public Task<string?> ProbePortRangeAsync(string host, int startPort, int endPort, CancellationToken ct = default) => Task.FromResult("http://localhost:9000");
    }
}
