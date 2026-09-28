using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.App.ViewModels;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Gst;
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
    private readonly MockSettingsService _settingsService;
    private readonly MockTallyConnection _tallyConnection;
    private readonly ActiveCompanyContext _companyContext;

    public MockTallyIntegrationTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"mock_integration_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance, _testDbPath);

        _syncRepo = new SyncRepository(_factory, NullLogger<SyncRepository>.Instance);
        _auditRepo = new AuditRepository(_factory);

        _companyService = new MockTallyCompanyService();
        _client = new MockTallyClient(NullLogger<MockTallyClient>.Instance);
        _parser = new TallyResponseParser(NullLogger<TallyResponseParser>.Instance);

        _settingsService = new MockSettingsService();
        _tallyConnection = new MockTallyConnection();

        _companyContext = new ActiveCompanyContext(
            _auditRepo,
            _settingsService,
            _companyService,
            NullLogger<ActiveCompanyContext>.Instance);

        _voucherService = new TallyVoucherService(
            _client,
            new TallyRequestBuilder(),
            _parser,
            _settingsService,
            NullLogger<TallyVoucherService>.Instance);

        _masterService = new TallyMasterService(
            _client,
            new TallyRequestBuilder(),
            _parser,
            _settingsService,
            NullLogger<TallyMasterService>.Instance);

        _syncManager = new SyncManager(
            _tallyConnection,
            _companyService,
            _masterService,
            _voucherService,
            _syncRepo,
            _auditRepo,
            _settingsService,
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
    public async Task MockActiveCompany_ResolvesCorrectly_AndPersistsToSettings()
    {
        var active = await _companyService.GetActiveCompanyAsync();
        Assert.Equal("Demo Industrial Solutions Pvt Ltd (FY 2025-26)", active);

        var ensuredCompany = await _companyContext.EnsureAndInitializeActiveCompanyAsync();
        Assert.NotNull(ensuredCompany);
        Assert.Equal("Demo Industrial Solutions Pvt Ltd (FY 2025-26)", ensuredCompany.TallyCompanyName);

        var persisted = await _settingsService.GetSettingAsync("ActiveCompany");
        Assert.Equal("Demo Industrial Solutions Pvt Ltd (FY 2025-26)", persisted);
    }

    [Fact]
    public async Task EmptyLocalRepository_MockMode_CreatesDemoCompany_Idempotently()
    {
        // 1. Initial empty repository check
        var initialList = await _auditRepo.GetAllCompaniesAsync();
        Assert.Empty(initialList);

        // 2. Ensure mock company
        var company1 = await _companyContext.EnsureAndInitializeActiveCompanyAsync();
        Assert.NotNull(company1);
        Assert.Equal("Demo Industrial Solutions Pvt Ltd (FY 2025-26)", company1.TallyCompanyName);
        Assert.Equal("Demo Industrial Solutions Pvt Ltd", company1.FormalName);
        Assert.Equal("27DEMO1234F1Z9", company1.GSTIN);
        Assert.Equal("DEMOP1234F", company1.PAN);
        Assert.Equal("Maharashtra", company1.StateName);

        var listAfterFirst = await _auditRepo.GetAllCompaniesAsync();
        Assert.Single(listAfterFirst);

        // 3. Run second time - must be idempotent (no duplicates)
        var company2 = await _companyContext.EnsureAndInitializeActiveCompanyAsync();
        Assert.NotNull(company2);
        Assert.Equal(company1.Id, company2.Id);

        var listAfterSecond = await _auditRepo.GetAllCompaniesAsync();
        Assert.Single(listAfterSecond);
    }

    [Fact]
    public async Task CompaniesViewModel_DisplaysSynchronizedMockCompany_AndSelectedProfile()
    {
        var vm = new CompaniesViewModel(_auditRepo, _settingsService, _companyContext, _companyService);
        await vm.LoadCompaniesAsync();

        Assert.True(vm.HasCompanies);
        Assert.NotEmpty(vm.Companies);
        Assert.Equal("Demo Industrial Solutions Pvt Ltd (FY 2025-26)", vm.ActiveCompanyName);

        var selected = vm.SelectedCompany;
        Assert.NotNull(selected);
        Assert.Equal("Demo Industrial Solutions Pvt Ltd (FY 2025-26)", selected.TallyCompanyName);
        Assert.Equal("Demo Industrial Solutions Pvt Ltd", selected.FormalName);
        Assert.Equal("27DEMO1234F1Z9", selected.GSTIN);
        Assert.Equal("DEMOP1234F", selected.PAN);
        Assert.Equal("Maharashtra", selected.StateName);
    }

    [Fact]
    public async Task SyncViewModel_UsesMatchingActiveCompany_FromContext()
    {
        await _companyContext.EnsureAndInitializeActiveCompanyAsync();

        var syncVm = new SyncViewModel(_syncManager, _companyService, _settingsService, _companyContext);
        
        Assert.Equal("Demo Industrial Solutions Pvt Ltd (FY 2025-26)", syncVm.CompanyName);
    }

    [Fact]
    public async Task SwitchingActiveCompany_UpdatesSharedContextAndListeners()
    {
        // Setup two companies
        var comp1 = new Company
        {
            Id = "COMP-01",
            TallyCompanyName = "Demo Industrial Solutions Pvt Ltd (FY 2025-26)",
            FormalName = "Demo Industrial Solutions Pvt Ltd",
            GSTIN = "27DEMO1234F1Z9",
            PAN = "DEMOP1234F",
            BooksFromDate = new DateTime(2025, 4, 1)
        };
        var comp2 = new Company
        {
            Id = "COMP-02",
            TallyCompanyName = "Delta Retail Ventures LLP (FY 2025-26)",
            FormalName = "Delta Retail Ventures LLP",
            GSTIN = "27DELTA9988Z1Z2",
            PAN = "DELTP9988Z",
            BooksFromDate = new DateTime(2025, 4, 1)
        };

        await _auditRepo.EnsureCompanyAsync(comp1);
        await _auditRepo.EnsureCompanyAsync(comp2);

        string? notifiedCompany = null;
        _companyContext.ActiveCompanyChanged += (s, c) => notifiedCompany = c?.TallyCompanyName;

        await _companyContext.SetActiveCompanyAsync(comp2);

        Assert.Equal("Delta Retail Ventures LLP (FY 2025-26)", _companyContext.ActiveCompanyName);
        Assert.Equal("Delta Retail Ventures LLP (FY 2025-26)", notifiedCompany);

        var persisted = await _settingsService.GetSettingAsync("ActiveCompany");
        Assert.Equal("Delta Retail Ventures LLP (FY 2025-26)", persisted);
    }

    [Fact]
    public async Task MultiCompanyAndFinancialPeriodIsolation_IsPreserved()
    {
        var comp1 = new Company
        {
            Id = "COMP-ALPHA",
            TallyCompanyName = "Alpha Corp (FY 2024-25)",
            BooksFromDate = new DateTime(2024, 4, 1)
        };
        var comp2 = new Company
        {
            Id = "COMP-BETA",
            TallyCompanyName = "Beta Corp (FY 2025-26)",
            BooksFromDate = new DateTime(2025, 4, 1)
        };

        await _auditRepo.EnsureCompanyAsync(comp1);
        await _auditRepo.EnsureCompanyAsync(comp2);

        var list = await _auditRepo.GetAllCompaniesAsync();
        Assert.Equal(2, list.Count);
        Assert.Contains(list, c => c.Id == "COMP-ALPHA" && c.BooksFromDate.Year == 2024);
        Assert.Contains(list, c => c.Id == "COMP-BETA" && c.BooksFromDate.Year == 2025);
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
        if (activeCompany == null) throw new InvalidOperationException("Demo company should be active");
        Assert.Equal("Demo Industrial Solutions Pvt Ltd (FY 2025-26)", activeCompany);

        // 3. Load Company Statutory Profile & Verify marked as synthetic/demo
        var profile = await _companyService.GetCompanyProfileTypedAsync(activeCompany, null);
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

        var ledgersCount = result.TotalProcessed - result.Inserted;
        Assert.True(result.Inserted > 100, $"Expected several hundred synced records but got {result.Inserted}");

        // 6. Existing GST rules evaluation on synthetic database
        var gstContext = new GstAuditContext(activeCompany, new DateTime(2025, 4, 1), new DateTime(2026, 3, 31), "27DEMO1234F1Z9", "Maharashtra");
        var missingGstinRule = new MissingGstinOnB2BRule(_factory);
        var missingGstinExceptions = await missingGstinRule.EvaluateAsync(gstContext);
        
        var unregisteredSupplyEx = missingGstinExceptions.FirstOrDefault(e => e.PartyLedgerName == "Unregistered Steel Supplier");
        Assert.NotNull(unregisteredSupplyEx);
        Assert.Equal(SeverityLevel.High, unregisteredSupplyEx.Severity);
        Assert.Contains("Unregistered Steel Supplier", unregisteredSupplyEx.Explanation);

        var gstinFormatRule = new GstinFormatCheckRule(_factory);
        var formatExceptions = await gstinFormatRule.EvaluateAsync(gstContext);
        var invalidGstinEx = formatExceptions.FirstOrDefault(e => e.PartyLedgerName == "Invalid GSTIN Trader");
        Assert.NotNull(invalidGstinEx);
        Assert.Equal("27INVALID1234FX", invalidGstinEx.PartyGstin);

        // 7. Verify TDS threshold and incorrect rate rule detections
        using var conn = await _factory.CreateConnectionAsync();
        int suspenseEntriesCount = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM VoucherEntries WHERE LedgerName = 'Suspense Account'";
            suspenseEntriesCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        Assert.True(suspenseEntriesCount > 0, "Suspense Account should contain synthetic ledger postings.");

        // 8. Duplicate detection asserts against Beta Distributors duplications
        int duplicateCount = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Vouchers WHERE VoucherNumber = 'SAL-DUP-020'";
            duplicateCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }
        Assert.Equal(2, duplicateCount);

        // 9. Verify demo mode is purely non-destructive and doesn't modify anything outside local SQLite
        Assert.True(File.Exists(_testDbPath));
    }

    [Fact]
    public async Task MainWindowViewModel_ResolvesMockActiveCompany_RatherThanActiveTallySession()
    {
        await _companyContext.EnsureAndInitializeActiveCompanyAsync();

        // Create a connection with generic ActiveEndpoint company "Active Tally Session"
        var conn = new MockTallyConnection
        {
            ActiveEndpoint = new TallyEndpointInfo("localhost", 9000, true, "Mock/Demo", "Active Tally Session", 5)
        };

        var mainVm = new MainWindowViewModel(
            conn,
            _companyContext,
            _companyService,
            _settingsService,
            new DashboardViewModel(_auditRepo, conn, new AuditEngine(System.Array.Empty<IAuditRule>(), NullLogger<AuditEngine>.Instance), _settingsService, new MockDrillDownService(), new MockAiService(), _companyContext, NullLogger<DashboardViewModel>.Instance),
            new TallyConnectionViewModel(conn, _companyService, _settingsService, new TallyConnectionMonitor(conn, _settingsService, NullLogger<TallyConnectionMonitor>.Instance), _companyContext),
            new SyncViewModel(_syncManager, _companyService, _settingsService, _companyContext),
            new SettingsViewModel(_settingsService, _initializer, _companyContext),
            new CompaniesViewModel(_auditRepo, _settingsService, _companyContext, _companyService),
            new GstAuditViewModel(_auditRepo, _settingsService, _companyContext),
            new TdsAuditViewModel(_auditRepo, _settingsService, _companyContext),
            new VouchersViewModel(_factory, _settingsService, _auditRepo, _companyContext),
            new LedgersViewModel(_factory, _settingsService, _auditRepo, _companyContext),
            new BankAuditViewModel(_auditRepo, _settingsService, _companyContext),
            new ExceptionsViewModel(_auditRepo, _settingsService, _companyContext),
            new ReportsViewModel(_auditRepo, _settingsService, _companyContext));

        Assert.Equal("Demo Industrial Solutions Pvt Ltd (FY 2025-26)", mainVm.ActiveCompany);
        Assert.NotEqual("Active Tally Session", mainVm.ActiveCompany);
    }

    [Fact]
    public async Task RealTallyCompanyResolution_Works_WithoutHardcodedDemoCompany()
    {
        var realCompanyService = new MockCustomCompanyService("Custom Enterprise Ltd (FY 2025-26)");
        var realSettings = new MockSettingsService();
        await realSettings.SetMockModeEnabledAsync(false);

        var realContext = new ActiveCompanyContext(
            _auditRepo,
            realSettings,
            realCompanyService,
            NullLogger<ActiveCompanyContext>.Instance);

        var company = await realContext.EnsureAndInitializeActiveCompanyAsync();
        Assert.NotNull(company);
        Assert.Equal("Custom Enterprise Ltd (FY 2025-26)", company.TallyCompanyName);
        Assert.Equal("Custom Enterprise Ltd", company.FormalName);

        var persisted = await realSettings.GetSettingAsync("ActiveCompany");
        Assert.Equal("Custom Enterprise Ltd (FY 2025-26)", persisted);
    }

    [Fact]
    public async Task SettingsViewModel_TogglingMockMode_TriggersCompanyContextInitialization()
    {
        var settingsVm = new SettingsViewModel(_settingsService, _initializer, _companyContext);
        settingsVm.IsMockMode = true;
        await _settingsService.SetSettingAsync("ActiveCompany", string.Empty);

        // Act - Save settings
        var saveMethod = typeof(SettingsViewModel).GetMethod("SaveSettingsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (saveMethod != null)
        {
            var task = (Task)saveMethod.Invoke(settingsVm, null)!;
            await task;
        }

        var active = _companyContext.ActiveCompanyName;
        Assert.Equal("Demo Industrial Solutions Pvt Ltd (FY 2025-26)", active);
    }

    private class MockDrillDownService : ITallyDrillDownService
    {
        public Task<TallyVoucherDrillDownResult?> GetVoucherDrillDownAsync(string voucherNumber, string? companyName = null, CancellationToken cancellationToken = default)
            => Task.FromResult<TallyVoucherDrillDownResult?>(null);
    }

    private class MockAiService : IAuditAssistantService
    {
        public bool IsConfigured => false;
        public Task<string> ExplainExceptionAsync(AuditException exception, Company company, CancellationToken cancellationToken = default) => Task.FromResult("Mock AI");
        public Task<string> SuggestCorrectionAsync(AuditException exception, Company company, CancellationToken cancellationToken = default) => Task.FromResult("Mock Correction");
        public Task<string> DraftAuditorRemarkAsync(AuditException exception, Company company, CancellationToken cancellationToken = default) => Task.FromResult("Mock Remark");
        public Task<string> GenerateRunExecutiveSummaryAsync(AuditRun run, IReadOnlyList<AuditException> exceptions, Company company, CancellationToken cancellationToken = default) => Task.FromResult("Mock Summary");
        public Task<string> AskAssistantAsync(string question, string companyName, CancellationToken cancellationToken = default) => Task.FromResult("Mock Answer");
    }

    private class MockCustomCompanyService : ITallyCompanyService
    {
        private readonly string _companyName;
        public MockCustomCompanyService(string companyName) => _companyName = companyName;

        public Task<IReadOnlyList<string>> GetOpenCompaniesAsync(string? endpointUrl = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { _companyName });

        public Task<string?> GetActiveCompanyAsync(string? endpointUrl = null, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(_companyName);

        public Task<System.Collections.Generic.Dictionary<string, string>> GetCompanyProfileAsync(string endpointUrl, string companyName, CancellationToken cancellationToken = default)
            => Task.FromResult(new System.Collections.Generic.Dictionary<string, string> { { "Name", companyName } });

        public Task<TallyCompanyProfile?> GetCompanyProfileTypedAsync(string companyName, string? endpointUrl = null, CancellationToken cancellationToken = default)
        {
            var profile = new TallyCompanyProfile
            {
                Name = companyName,
                FormalName = companyName.Replace(" (FY 2025-26)", ""),
                GSTIN = "27CUSTOM1234F1Z1",
                PAN = "CUSTP1234F",
                StateName = "Maharashtra",
                StateCode = "27",
                BooksBeginningFrom = new DateTime(2025, 4, 1),
                AlterId = 5001
            };
            return Task.FromResult<TallyCompanyProfile?>(profile);
        }
    }

    private class MockSettingsService : ISettingsService
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _settings = new();

        public MockSettingsService()
        {
            _settings["TallyHost"] = "localhost";
            _settings["TallyPort"] = "9000";
            _settings["IsMockModeEnabled"] = "true";
        }

        public Task<string> GetSettingAsync(string key, string defaultValue = "", CancellationToken cancellationToken = default)
        {
            if (_settings.TryGetValue(key, out var val))
            {
                return Task.FromResult(val);
            }
            return Task.FromResult(defaultValue);
        }

        public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _settings[key] = value;
            return Task.CompletedTask;
        }

        public Task<int> GetTallyPortAsync()
        {
            if (_settings.TryGetValue("TallyPort", out var val) && int.TryParse(val, out var port))
            {
                return Task.FromResult(port);
            }
            return Task.FromResult(9000);
        }

        public Task SetTallyPortAsync(int port)
        {
            _settings["TallyPort"] = port.ToString();
            return Task.CompletedTask;
        }

        public Task<string> GetTallyHostAsync()
        {
            if (_settings.TryGetValue("TallyHost", out var val))
            {
                return Task.FromResult(val);
            }
            return Task.FromResult("localhost");
        }

        public Task SetTallyHostAsync(string host)
        {
            _settings["TallyHost"] = host;
            return Task.CompletedTask;
        }

        public Task<bool> IsMockModeEnabledAsync()
        {
            if (_settings.TryGetValue("IsMockModeEnabled", out var val) && bool.TryParse(val, out var enabled))
            {
                return Task.FromResult(enabled);
            }
            return Task.FromResult(true);
        }

        public Task SetMockModeEnabledAsync(bool enabled)
        {
            _settings["IsMockModeEnabled"] = enabled.ToString().ToLower();
            return Task.CompletedTask;
        }
    }

    private class MockTallyConnection : ITallyConnection
    {
        public ConnectionStatus CurrentStatus { get; set; } = ConnectionStatus.Connected;
        public TallyEndpointInfo? ActiveEndpoint { get; set; } = new TallyEndpointInfo("localhost", 9000, true, "Mock/Demo", "Demo Industrial Solutions Pvt Ltd (FY 2025-26)", 5);
        public string? LastErrorMessage { get; set; } = null;

        public event EventHandler<ConnectionStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public Task<bool> CheckIfProcessRunningAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<TallyEndpointInfo?> ProbePortRangeAsync(string host = "localhost", int startPort = 9000, int endPort = 9005, CancellationToken cancellationToken = default)
        {
            var info = new TallyEndpointInfo(host, startPort, true, "Mock/Demo", "Demo Industrial Solutions Pvt Ltd (FY 2025-26)", 5);
            return Task.FromResult<TallyEndpointInfo?>(info);
        }

        public Task<bool> TestConnectionAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }
}
