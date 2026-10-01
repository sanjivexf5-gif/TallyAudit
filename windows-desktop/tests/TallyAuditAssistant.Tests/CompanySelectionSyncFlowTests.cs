using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.App.Services;
using TallyAuditAssistant.App.ViewModels;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Ledgers;
using TallyAuditAssistant.Core.Domain.Sync;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Domain.Vouchers;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Core.Services;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine;
using TallyAuditAssistant.Engine.Services;
using TallyAuditAssistant.TallyIntegration;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class CompanySelectionSyncFlowTests
{
    private readonly Mock<ITallyConnection> _mockConnection;
    private readonly Mock<ITallyCompanyService> _mockCompanyService;
    private readonly Mock<ISettingsService> _mockSettings;
    private readonly Mock<IAuditRepository> _mockAuditRepo;
    private readonly Mock<ISyncRepository> _mockSyncRepo;
    private readonly Mock<ITallyMasterService> _mockMasterService;
    private readonly Mock<ITallyVoucherService> _mockVoucherService;
    private readonly NavigationService _navigationService;
    private readonly TallyConnectionMonitor _connectionMonitor;
    private readonly ActiveCompanyContext _companyContext;
    private readonly SqliteConnectionFactory _sqliteConnectionFactory;

    public CompanySelectionSyncFlowTests()
    {
        _mockConnection = new Mock<ITallyConnection>();
        _mockCompanyService = new Mock<ITallyCompanyService>();
        _mockSettings = new Mock<ISettingsService>();
        _mockAuditRepo = new Mock<IAuditRepository>();
        _mockSyncRepo = new Mock<ISyncRepository>();
        _mockMasterService = new Mock<ITallyMasterService>();
        _mockVoucherService = new Mock<ITallyVoucherService>();
        _navigationService = new NavigationService();

        var dbPath = Path.Combine(Path.GetTempPath(), $"test_sync_flow_{Guid.NewGuid():N}.db");
        _sqliteConnectionFactory = new SqliteConnectionFactory(dbPath);

        var settingsDict = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        _mockSettings.Setup(s => s.GetTallyHostAsync()).ReturnsAsync("localhost");
        _mockSettings.Setup(s => s.GetTallyPortAsync()).ReturnsAsync(9000);
        _mockSettings.Setup(s => s.IsMockModeEnabledAsync()).ReturnsAsync(false);
        _mockSettings.Setup(s => s.SetSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .Callback<string, string, CancellationToken>((key, val, ct) => settingsDict[key] = val)
                     .Returns(Task.CompletedTask);
        _mockSettings.Setup(s => s.GetSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((string key, string def, CancellationToken ct) => settingsDict.TryGetValue(key, out var val) ? val : def);

        _connectionMonitor = new TallyConnectionMonitor(_mockConnection.Object, _mockSettings.Object, NullLogger<TallyConnectionMonitor>.Instance);
        _companyContext = new ActiveCompanyContext(_mockAuditRepo.Object, _mockSettings.Object, _mockCompanyService.Object);

        _mockAuditRepo.Setup(r => r.EnsureCompanyAsync(It.IsAny<Company>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync((Company c, CancellationToken ct) => c);
        _mockAuditRepo.Setup(r => r.GetCompanyByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync((string name, CancellationToken ct) => null);
    }

    private MainWindowViewModel CreateMainWindowViewModel(SyncViewModel syncVM, TallyConnectionViewModel connVM)
    {
        var dashboardVM = new DashboardViewModel(
            _mockAuditRepo.Object,
            _mockConnection.Object,
            new Mock<IAuditEngine>().Object,
            _mockSettings.Object,
            new Mock<ITallyDrillDownService>().Object,
            new Mock<IAuditAssistantService>().Object,
            _companyContext,
            NullLogger<DashboardViewModel>.Instance);

        var settingsVM = new SettingsViewModel(
            _mockSettings.Object,
            new Mock<IDatabaseInitializer>().Object,
            _companyContext,
            new Mock<IUpdateService>().Object,
            _mockAuditRepo.Object,
            _mockConnection.Object);

        var companiesVM = new CompaniesViewModel(
            _mockAuditRepo.Object,
            _mockSettings.Object,
            _companyContext,
            _mockCompanyService.Object,
            _navigationService);

        var gstVM = new GstAuditViewModel(
            _mockAuditRepo.Object,
            _mockSettings.Object,
            _companyContext,
            _navigationService,
            NullLogger<GstAuditViewModel>.Instance);

        var tdsVM = new TdsAuditViewModel(
            _mockAuditRepo.Object,
            _mockSettings.Object,
            _companyContext,
            _navigationService,
            NullLogger<TdsAuditViewModel>.Instance);

        var vouchersVM = new VouchersViewModel(
            _sqliteConnectionFactory,
            _mockSettings.Object,
            _mockAuditRepo.Object,
            _companyContext,
            _navigationService);

        var ledgersVM = new LedgersViewModel(
            _sqliteConnectionFactory,
            _mockSettings.Object,
            _mockAuditRepo.Object,
            _companyContext,
            _navigationService);

        var bankVM = new BankAuditViewModel(
            _mockAuditRepo.Object,
            _mockSettings.Object,
            _companyContext,
            _navigationService);

        var exceptionsVM = new ExceptionsViewModel(
            _mockAuditRepo.Object,
            _mockSettings.Object,
            _companyContext,
            _navigationService,
            null,
            NullLogger<ExceptionsViewModel>.Instance);

        var reportsVM = new ReportsViewModel(
            _mockAuditRepo.Object,
            _mockSettings.Object,
            _companyContext,
            _navigationService);

        var investigationVM = new InvestigationViewModel(
            new Mock<IInvestigationService>().Object,
            _mockAuditRepo.Object,
            _companyContext,
            _navigationService);

        return new MainWindowViewModel(
            _mockConnection.Object,
            _companyContext,
            _mockCompanyService.Object,
            _mockSettings.Object,
            _navigationService,
            dashboardVM,
            connVM,
            syncVM,
            settingsVM,
            companiesVM,
            gstVM,
            tdsVM,
            vouchersVM,
            ledgersVM,
            bankVM,
            exceptionsVM,
            reportsVM,
            investigationVM);
    }

    [Fact]
    public async Task CompanyDiscovery_ReturnsCompany()
    {
        // 1. CompanyDiscovery_ReturnsCompany
        var endpoint = new TallyEndpointInfo("localhost", 9000, true, "TallyPrime", null, 2, null, ConnectionFailureCause.None);
        _mockConnection.Setup(c => c.CheckIfProcessRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockConnection.Setup(c => c.DiscoverTallyAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(endpoint);
        _mockCompanyService.Setup(c => c.GetOpenCompaniesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(new List<string> { "Sanjiv Sinha Pvt Ltd" });

        var vm = new TallyConnectionViewModel(_mockConnection.Object, _mockCompanyService.Object, _mockSettings.Object, _connectionMonitor, _companyContext, _mockMasterService.Object, _mockVoucherService.Object, NullLogger<TallyConnectionViewModel>.Instance);
        await vm.ScanForTallyCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.Single(vm.AvailableCompanies);
        Assert.Contains("Sanjiv Sinha Pvt Ltd", vm.AvailableCompanies);
    }

    [Fact]
    public async Task SingleDiscoveredCompany_IsAutomaticallySelected()
    {
        // 2. SingleDiscoveredCompany_IsAutomaticallySelected
        var endpoint = new TallyEndpointInfo("localhost", 9000, true, "TallyPrime", null, 2, null, ConnectionFailureCause.None);
        _mockConnection.Setup(c => c.CheckIfProcessRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockConnection.Setup(c => c.DiscoverTallyAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(endpoint);
        _mockCompanyService.Setup(c => c.GetOpenCompaniesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(new List<string> { "Sanjiv Sinha Pvt Ltd" });

        var vm = new TallyConnectionViewModel(_mockConnection.Object, _mockCompanyService.Object, _mockSettings.Object, _connectionMonitor, _companyContext, _mockMasterService.Object, _mockVoucherService.Object, NullLogger<TallyConnectionViewModel>.Instance);
        await vm.ScanForTallyCommand.ExecuteAsync(null);
        await vm.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        Assert.Equal("Sanjiv Sinha Pvt Ltd", vm.SelectedCompany);
        Assert.Equal("Sanjiv Sinha Pvt Ltd", vm.ActiveCompany);
    }

    [Fact]
    public async Task SelectedCompany_UpdatesActiveCompanyContext()
    {
        // 3. SelectedCompany_UpdatesActiveCompanyContext
        var endpoint = new TallyEndpointInfo("localhost", 9000, true, "TallyPrime", null, 2, null, ConnectionFailureCause.None);
        _mockConnection.Setup(c => c.CheckIfProcessRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockConnection.Setup(c => c.DiscoverTallyAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(endpoint);
        _mockCompanyService.Setup(c => c.GetOpenCompaniesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(new List<string> { "Sanjiv Sinha Pvt Ltd" });

        var vm = new TallyConnectionViewModel(_mockConnection.Object, _mockCompanyService.Object, _mockSettings.Object, _connectionMonitor, _companyContext, _mockMasterService.Object, _mockVoucherService.Object, NullLogger<TallyConnectionViewModel>.Instance);
        await vm.ScanForTallyCommand.ExecuteAsync(null);
        await vm.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        Assert.Equal("Sanjiv Sinha Pvt Ltd", _companyContext.ActiveCompanyName);
        Assert.Equal("Sanjiv Sinha Pvt Ltd", _companyContext.TallyCompanyName);
    }

    [Fact]
    public async Task SelectedCompany_UpdatesHeader()
    {
        // 4. SelectedCompany_UpdatesHeader
        var syncManager = new SyncManager(_mockConnection.Object, _mockCompanyService.Object, _mockMasterService.Object, _mockVoucherService.Object, _mockSyncRepo.Object, _mockAuditRepo.Object, _mockSettings.Object, NullLogger<SyncManager>.Instance, _companyContext);
        var syncVM = new SyncViewModel(syncManager, _mockCompanyService.Object, _mockSettings.Object, _companyContext);
        var connVM = new TallyConnectionViewModel(_mockConnection.Object, _mockCompanyService.Object, _mockSettings.Object, _connectionMonitor, _companyContext, _mockMasterService.Object, _mockVoucherService.Object, NullLogger<TallyConnectionViewModel>.Instance);
        var mainVM = CreateMainWindowViewModel(syncVM, connVM);

        await _companyContext.SetActiveCompanyNameAsync("Sanjiv Sinha Pvt Ltd");

        Assert.Equal("Sanjiv Sinha Pvt Ltd", mainVM.ActiveCompany);
    }

    [Fact]
    public async Task SelectedCompany_SurvivesNavigation()
    {
        // 5. SelectedCompany_SurvivesNavigation
        var syncManager = new SyncManager(_mockConnection.Object, _mockCompanyService.Object, _mockMasterService.Object, _mockVoucherService.Object, _mockSyncRepo.Object, _mockAuditRepo.Object, _mockSettings.Object, NullLogger<SyncManager>.Instance, _companyContext);
        var syncVM = new SyncViewModel(syncManager, _mockCompanyService.Object, _mockSettings.Object, _companyContext);
        var connVM = new TallyConnectionViewModel(_mockConnection.Object, _mockCompanyService.Object, _mockSettings.Object, _connectionMonitor, _companyContext, _mockMasterService.Object, _mockVoucherService.Object, NullLogger<TallyConnectionViewModel>.Instance);
        var mainVM = CreateMainWindowViewModel(syncVM, connVM);

        await _companyContext.SetActiveCompanyNameAsync("Sanjiv Sinha Pvt Ltd");

        mainVM.Navigate("Sync");
        Assert.Equal("Sanjiv Sinha Pvt Ltd", mainVM.ActiveCompany);

        mainVM.Navigate("Vouchers");
        Assert.Equal("Sanjiv Sinha Pvt Ltd", mainVM.ActiveCompany);

        mainVM.Navigate("Sync");
        Assert.Equal("Sanjiv Sinha Pvt Ltd", mainVM.ActiveCompany);
        Assert.Equal("Sanjiv Sinha Pvt Ltd", syncVM.CompanyName);
    }

    [Fact]
    public async Task Synchronize_WhenCompanySelected_Starts()
    {
        // 6. Synchronize_WhenCompanySelected_Starts
        var mockSyncManager = new Mock<ISyncManager>();
        mockSyncManager.Setup(m => m.StartSyncAsync(It.IsAny<string>(), It.IsAny<SyncMode>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new SyncResult(true, SyncMode.Full, 10, 10, 0, 0, 0, TimeSpan.FromSeconds(1)));

        var syncVM = new SyncViewModel(mockSyncManager.Object, _mockCompanyService.Object, _mockSettings.Object, _companyContext);

        await _companyContext.SetActiveCompanyNameAsync("Sanjiv Sinha Pvt Ltd");

        await syncVM.StartFullSyncCommand.ExecuteAsync(null);

        mockSyncManager.Verify(m => m.StartSyncAsync("Sanjiv Sinha Pvt Ltd", SyncMode.Full, It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain("Please select a Tally company", syncVM.CurrentTaskDescription);
    }

    [Fact]
    public async Task Synchronize_WhenNoCompanySelected_IsBlocked()
    {
        // 7. Synchronize_WhenNoCompanySelected_IsBlocked
        var mockSyncManager = new Mock<ISyncManager>();
        var syncVM = new SyncViewModel(mockSyncManager.Object, _mockCompanyService.Object, _mockSettings.Object, _companyContext);

        await _companyContext.ClearActiveCompanyAsync();

        await syncVM.StartFullSyncCommand.ExecuteAsync(null);

        mockSyncManager.Verify(m => m.StartSyncAsync(It.IsAny<string>(), It.IsAny<SyncMode>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains("Please select and save a Tally company before synchronization.", syncVM.CurrentTaskDescription);
    }

    [Fact]
    public async Task Synchronize_UsesActiveCompanyContext()
    {
        // 8. Synchronize_UsesActiveCompanyContext
        var mockSyncManager = new Mock<ISyncManager>();
        mockSyncManager.Setup(m => m.StartSyncAsync(It.IsAny<string>(), It.IsAny<SyncMode>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new SyncResult(true, SyncMode.Full, 0, 0, 0, 0, 0, TimeSpan.FromSeconds(1)));

        var syncVM = new SyncViewModel(mockSyncManager.Object, _mockCompanyService.Object, _mockSettings.Object, _companyContext);

        await _companyContext.SetActiveCompanyNameAsync("Sanjiv Sinha Pvt Ltd");

        await syncVM.StartFullSyncCommand.ExecuteAsync(null);

        mockSyncManager.Verify(m => m.StartSyncAsync(_companyContext.ActiveCompanyName, SyncMode.Full, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void TallyQuery_UsesSelectedCompany()
    {
        // 9. TallyQuery_UsesSelectedCompany
        var builder = new TallyRequestBuilder();
        var xml = builder.BuildLedgerCollectionRequest("Sanjiv Sinha Pvt Ltd");

        Assert.Contains("Sanjiv Sinha Pvt Ltd", xml);
    }

    [Fact]
    public void TallyQuery_ContainsSVCurrentCompany()
    {
        // 10. TallyQuery_ContainsSVCurrentCompany
        var builder = new TallyRequestBuilder();
        var xml = builder.BuildVoucherCollectionRequest("Sanjiv Sinha Pvt Ltd", new DateTime(2025, 4, 1), new DateTime(2026, 3, 31));

        Assert.Contains("<SVCurrentCompany>Sanjiv Sinha Pvt Ltd</SVCurrentCompany>", xml);
    }

    [Fact]
    public async Task ChangingCompany_UpdatesContext()
    {
        // 11. ChangingCompany_UpdatesContext
        await _companyContext.SetActiveCompanyNameAsync("First Company");
        Assert.Equal("First Company", _companyContext.ActiveCompanyName);

        await _companyContext.SetActiveCompanyNameAsync("Second Company");
        Assert.Equal("Second Company", _companyContext.ActiveCompanyName);
        Assert.Equal("Second Company", _companyContext.TallyCompanyName);
    }

    [Fact]
    public async Task ChangingCompany_DoesNotRetainPreviousCompany()
    {
        // 12. ChangingCompany_DoesNotRetainPreviousCompany
        var comp1 = new Company { Id = "COMP-1", TallyCompanyName = "Company 1", BooksFromDate = new DateTime(2024, 4, 1) };
        var comp2 = new Company { Id = "COMP-2", TallyCompanyName = "Company 2", BooksFromDate = new DateTime(2025, 4, 1) };

        await _companyContext.SetActiveCompanyAsync(comp1);
        Assert.Equal("Company 1", _companyContext.ActiveCompanyName);
        Assert.Equal("COMP-1-FY2024", _companyContext.CurrentPeriod?.Id);

        await _companyContext.SetActiveCompanyAsync(comp2);
        Assert.Equal("Company 2", _companyContext.ActiveCompanyName);
        Assert.NotEqual("Company 1", _companyContext.ActiveCompanyName);
        Assert.Equal("COMP-2-FY2025", _companyContext.CurrentPeriod?.Id);
    }

    [Fact]
    public async Task FinancialPeriodRemainsConsistentWithCompany()
    {
        // 13. FinancialPeriodRemainsConsistentWithCompany
        var comp = new Company { Id = "COMP-ALPHA", TallyCompanyName = "Alpha Corp", BooksFromDate = new DateTime(2025, 4, 1) };
        await _companyContext.SetActiveCompanyAsync(comp);

        var period = await _companyContext.GetActivePeriodAsync();
        Assert.NotNull(period);
        Assert.Equal("COMP-ALPHA", period.CompanyId);
        Assert.Equal(new DateTime(2025, 4, 1), period.StartDate);
        Assert.Equal(new DateTime(2026, 3, 31), period.EndDate);
    }

    [Fact]
    public async Task SyncManager_UsesCurrentCompany()
    {
        // 14. SyncManager_UsesCurrentCompany
        _mockConnection.Setup(c => c.TestConnectionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(true);
        _mockCompanyService.Setup(c => c.GetCompanyProfileTypedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(new TallyCompanyProfile { Name = "Sanjiv Sinha Pvt Ltd", BooksBeginningFrom = new DateTime(2025, 4, 1) });
        _mockMasterService.Setup(m => m.GetGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(new List<string> { "Sundry Debtors" });
        _mockMasterService.Setup(m => m.GetLedgersAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(new List<TallyLedgerDto>());

        async IAsyncEnumerable<TallyVoucherDto> EmptyVouchers() { await Task.Yield(); yield break; }
        _mockVoucherService.Setup(v => v.StreamVouchersChunkedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                           .Returns(EmptyVouchers());

        var syncManager = new SyncManager(_mockConnection.Object, _mockCompanyService.Object, _mockMasterService.Object, _mockVoucherService.Object, _mockSyncRepo.Object, _mockAuditRepo.Object, _mockSettings.Object, NullLogger<SyncManager>.Instance, _companyContext);

        string? loggedCompany = null;
        syncManager.SyncLogEmitted += (s, log) =>
        {
            if (log.Contains("Synchronization starting for company:"))
            {
                loggedCompany = log;
            }
        };

        var result = await syncManager.StartSyncAsync("Sanjiv Sinha Pvt Ltd", SyncMode.Full);

        Assert.NotNull(result);
        Assert.NotNull(loggedCompany);
        Assert.Contains("Sanjiv Sinha Pvt Ltd", loggedCompany);
    }

    [Fact]
    public async Task NoDemoFallback_WhenRealTallyConnected()
    {
        // 15. NoDemoFallback_WhenRealTallyConnected
        _mockSettings.Setup(s => s.IsMockModeEnabledAsync()).ReturnsAsync(false);

        var comp = await _companyContext.GetActiveCompanyAsync();
        Assert.Null(comp); // Must NOT return fake or demo company

        await _companyContext.SetActiveCompanyNameAsync("Sanjiv Sinha Pvt Ltd");
        var active = await _companyContext.GetActiveCompanyAsync();

        Assert.NotNull(active);
        Assert.False(active.IsMock);
        Assert.Equal("Sanjiv Sinha Pvt Ltd", active.TallyCompanyName);
    }

    [Fact]
    public async Task EndToEnd_CompanySelection_To_Synchronization_WithRealTallyArchitecture()
    {
        // 16. Integration: Connect -> Discover -> Select -> Context Populated -> Navigate to Sync -> Start Full Sync -> SyncManager receives company
        var endpoint = new TallyEndpointInfo("localhost", 9000, true, "TallyPrime 4.0", null, 2, null, ConnectionFailureCause.None);
        _mockConnection.Setup(c => c.CheckIfProcessRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockConnection.Setup(c => c.DiscoverTallyAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(endpoint);
        _mockCompanyService.Setup(c => c.GetOpenCompaniesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(new List<string> { "Sanjiv Sinha Pvt Ltd" });

        _mockConnection.Setup(c => c.TestConnectionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(true);
        _mockCompanyService.Setup(c => c.GetCompanyProfileTypedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(new TallyCompanyProfile { Name = "Sanjiv Sinha Pvt Ltd", BooksBeginningFrom = new DateTime(2025, 4, 1) });
        _mockMasterService.Setup(m => m.GetGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(new List<string> { "Sundry Debtors" });
        _mockMasterService.Setup(m => m.GetLedgersAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(new List<TallyLedgerDto>
                          {
                              new TallyLedgerDto { Name = "Customer A", ParentGroup = "Sundry Debtors", OpeningBalance = 0, ClosingBalance = 10000 }
                          });

        async IAsyncEnumerable<TallyVoucherDto> MockVouchers()
        {
            await Task.Yield();
            yield return new TallyVoucherDto
            {
                Guid = "V-101",
                VoucherNumber = "V-101",
                VoucherType = "Sales",
                VoucherDate = new DateTime(2025, 4, 15),
                PartyLedgerName = "Customer A",
                TotalAmount = 10000,
                Entries = new List<TallyVoucherEntryDto>
                {
                    new TallyVoucherEntryDto { LedgerName = "Customer A", Amount = 10000, IsDebit = true },
                    new TallyVoucherEntryDto { LedgerName = "Sales", Amount = -10000, IsDebit = false }
                }
            };
        }
        _mockVoucherService.Setup(v => v.StreamVouchersChunkedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                           .Returns(MockVouchers());

        _mockSyncRepo.Setup(r => r.BatchUpsertGroupsAsync(It.IsAny<IReadOnlyList<Group>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(1);
        _mockSyncRepo.Setup(r => r.BatchUpsertLedgersAsync(It.IsAny<IReadOnlyList<Ledger>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((1, 0));
        _mockSyncRepo.Setup(r => r.BatchUpsertVouchersAsync(It.IsAny<IReadOnlyList<Voucher>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((1, 0));

        var syncManager = new SyncManager(_mockConnection.Object, _mockCompanyService.Object, _mockMasterService.Object, _mockVoucherService.Object, _mockSyncRepo.Object, _mockAuditRepo.Object, _mockSettings.Object, NullLogger<SyncManager>.Instance, _companyContext);
        var syncVM = new SyncViewModel(syncManager, _mockCompanyService.Object, _mockSettings.Object, _companyContext);
        var connVM = new TallyConnectionViewModel(_mockConnection.Object, _mockCompanyService.Object, _mockSettings.Object, _connectionMonitor, _companyContext, _mockMasterService.Object, _mockVoucherService.Object, NullLogger<TallyConnectionViewModel>.Instance);
        var mainVM = CreateMainWindowViewModel(syncVM, connVM);

        // 1. Scan for Tally
        await connVM.ScanForTallyCommand.ExecuteAsync(null);

        // 1.5 Commit company selection explicitly (WPF Select & Save Company button)
        await connVM.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // 2. Verified company selected & context updated
        Assert.Equal("Sanjiv Sinha Pvt Ltd", connVM.SelectedCompany);
        Assert.Equal("Sanjiv Sinha Pvt Ltd", _companyContext.ActiveCompanyName);
        Assert.Equal("Sanjiv Sinha Pvt Ltd", mainVM.ActiveCompany);

        // 3. Navigate to Sync
        mainVM.Navigate("Sync");
        Assert.Equal("Sanjiv Sinha Pvt Ltd", mainVM.ActiveCompany);
        Assert.Equal("Sanjiv Sinha Pvt Ltd", syncVM.CompanyName);

        // 4. Start Full Sync
        await syncVM.StartFullSyncCommand.ExecuteAsync(null);

        // 5. Verify stage progressed and completed without "Please select a Tally company"
        Assert.DoesNotContain("Please select a Tally company", syncVM.CurrentTaskDescription);
        Assert.Contains("SYNC SUCCESS", syncVM.CurrentTaskDescription);
    }

    [Fact]
    public async Task RealApplication_DI_Lifetime_EndToEnd()
    {
        // Integration test using real DI registrations via ApplicationServiceRegistration
        var services = new ServiceCollection();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_di_flow_{Guid.NewGuid():N}.db");
        services.AddApplicationServices(config, dbPath);

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync();

        // 1. Verify Singleton identity of IActiveCompanyContext
        var ctx1 = provider.GetRequiredService<IActiveCompanyContext>();
        var ctx2 = provider.GetRequiredService<IActiveCompanyContext>();
        Assert.Same(ctx1, ctx2);

        // Verify key services resolve cleanly
        Assert.NotNull(provider.GetRequiredService<ITallyClient>());
        Assert.NotNull(provider.GetRequiredService<ITallyCompanyService>());
        Assert.NotNull(provider.GetRequiredService<ITallyMasterService>());
        Assert.NotNull(provider.GetRequiredService<ITallyVoucherService>());
        Assert.NotNull(provider.GetRequiredService<ISyncManager>());

        var connVM = provider.GetRequiredService<TallyConnectionViewModel>();
        var mainVM = provider.GetRequiredService<MainWindowViewModel>();
        var syncVM = provider.GetRequiredService<SyncViewModel>();

        // 2. Select RAVI & CO.
        connVM.SelectedCompany = "RAVI & CO.";

        // 3. Save & Activate
        await connVM.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // 4. Verify ActiveCompanyContext and Header
        Assert.Equal("RAVI & CO.", ctx1.ActiveCompanyName);
        Assert.Equal("RAVI & CO.", mainVM.ActiveCompany);
        Assert.Equal("Verified", connVM.PersistenceStatus);
        Assert.Equal("Verified", connVM.ContextStatus);

        // 5. Navigate to Sync
        mainVM.Navigate("Sync");
        await syncVM.OnNavigatedToAsync();

        Assert.Equal("RAVI & CO.", syncVM.CompanyName);
        Assert.StartsWith("Ready to synchronize RAVI & CO.", syncVM.CurrentTaskDescription);
        Assert.Equal("Context: Verified", syncVM.ContextStatusText);
    }

    [Fact]
    public async Task PersistenceRestart_RestoresActiveCompany()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_persistence_restart_{Guid.NewGuid():N}.db");
        var sqliteFactory = new SqliteConnectionFactory(dbPath);
        var initializer = new DatabaseInitializer(sqliteFactory, NullLogger<DatabaseInitializer>.Instance, dbPath);
        await initializer.InitializeAsync();

        var auditRepo = new AuditRepository(sqliteFactory);
        var settingsRepo = new SettingsRepository(sqliteFactory);
        var mockCompanyService = new Mock<ITallyCompanyService>();

        // Session 1: Activate Company A
        var ctx1 = new ActiveCompanyContext(auditRepo, settingsRepo, mockCompanyService.Object);
        await ctx1.SetActiveCompanyNameAsync("Company A");
        Assert.Equal("Company A", ctx1.ActiveCompanyName);

        // Session 2: Fresh context reading from same DB
        var ctx2 = new ActiveCompanyContext(auditRepo, settingsRepo, mockCompanyService.Object);
        var restored = await ctx2.GetActiveCompanyAsync();
        Assert.NotNull(restored);
        Assert.Equal("Company A", restored.TallyCompanyName);

        var syncVM = new SyncViewModel(new Mock<ISyncManager>().Object, mockCompanyService.Object, settingsRepo, ctx2);
        await syncVM.OnNavigatedToAsync();
        Assert.Equal("Company A", syncVM.CompanyName);
        Assert.StartsWith("Ready to synchronize Company A", syncVM.CurrentTaskDescription);
    }

    [Fact]
    public async Task MainWindowRefresh_DoesNotOverwriteCommittedCompany()
    {
        var services = new ServiceCollection();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_race_{Guid.NewGuid():N}.db");
        services.AddApplicationServices(config, dbPath);

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync();

        var ctx = provider.GetRequiredService<IActiveCompanyContext>();
        var mainVM = provider.GetRequiredService<MainWindowViewModel>();
        var connVM = provider.GetRequiredService<TallyConnectionViewModel>();

        // Trigger background refresh
        var refreshTask = mainVM.RefreshActiveCompanyAsync();

        // Commit company in parallel
        connVM.SelectedCompany = "RAVI & CO.";
        await connVM.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // Await pending refresh
        await refreshTask;

        // Verify it was not overwritten with "No Company Selected"
        Assert.Equal("RAVI & CO.", mainVM.ActiveCompany);
        Assert.Equal("RAVI & CO.", ctx.ActiveCompanyName);
    }

    [Fact]
    public async Task MainWindow_StaleRefresh_CannotOverwriteCommittedCompany()
    {
        var services = new ServiceCollection();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_stale_main_{Guid.NewGuid():N}.db");
        services.AddApplicationServices(config, dbPath);

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync();

        var ctx = provider.GetRequiredService<IActiveCompanyContext>();
        var mainVM = provider.GetRequiredService<MainWindowViewModel>();
        var connVM = provider.GetRequiredService<TallyConnectionViewModel>();

        // 1. Start refresh when DB has no company yet
        var refreshTask = mainVM.RefreshActiveCompanyAsync();

        // 2. Commit company concurrently
        connVM.SelectedCompany = "RAVI & CO.";
        await connVM.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // 3. Let initial refresh finish
        await refreshTask;

        // 4. Verify ActiveCompany never reverted to "No Company Selected"
        Assert.Equal("RAVI & CO.", mainVM.ActiveCompany);
        Assert.Equal("RAVI & CO.", ctx.ActiveCompanyName);
    }

    [Fact]
    public async Task SyncViewModel_StaleInitialLoad_CannotOverwriteCommittedCompany()
    {
        var services = new ServiceCollection();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_stale_sync_{Guid.NewGuid():N}.db");
        services.AddApplicationServices(config, dbPath);

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync();

        var ctx = provider.GetRequiredService<IActiveCompanyContext>();
        var syncVM = provider.GetRequiredService<SyncViewModel>();
        var connVM = provider.GetRequiredService<TallyConnectionViewModel>();

        // 1. Trigger initial load in background
        var loadTask = syncVM.OnNavigatedToAsync();

        // 2. Commit company concurrently
        connVM.SelectedCompany = "RAVI & CO.";
        await connVM.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // 3. Await load
        await loadTask;

        // 4. Assert
        Assert.Equal("RAVI & CO.", syncVM.CompanyName);
        Assert.Equal("RAVI & CO.", ctx.ActiveCompanyName);
        Assert.StartsWith("Ready to synchronize RAVI & CO.", syncVM.CurrentTaskDescription);
        Assert.Equal("Context: Verified", syncVM.ContextStatusText);
    }

    [Fact]
    public async Task SaveCompany_CannotBeOverwrittenByOlderRefresh()
    {
        var services = new ServiceCollection();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_older_refresh_{Guid.NewGuid():N}.db");
        services.AddApplicationServices(config, dbPath);

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync();

        var ctx = provider.GetRequiredService<IActiveCompanyContext>();
        var connVM = provider.GetRequiredService<TallyConnectionViewModel>();
        var mainVM = provider.GetRequiredService<MainWindowViewModel>();
        var syncVM = provider.GetRequiredService<SyncViewModel>();

        // 1. Start discovery refresh
        var refreshTask = connVM.RefreshCompaniesCommand.ExecuteAsync(null);

        // 2. User commits RAVI & CO.
        connVM.SelectedCompany = "RAVI & CO.";
        await connVM.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // 3. Let older refresh finish
        await refreshTask;

        // 4. Verify RAVI & CO. remains intact everywhere
        Assert.Equal("RAVI & CO.", ctx.ActiveCompanyName);
        Assert.Equal("RAVI & CO.", connVM.ActiveCompany);
        Assert.Equal("RAVI & CO.", mainVM.ActiveCompany);
        Assert.Equal("Verified", connVM.PersistenceStatus);
        Assert.Equal("Verified", connVM.ContextStatus);
    }

    [Fact]
    public async Task SaveCompany_RemainsAfterNavigation()
    {
        var services = new ServiceCollection();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_nav_{Guid.NewGuid():N}.db");
        services.AddApplicationServices(config, dbPath);

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync();

        var ctx = provider.GetRequiredService<IActiveCompanyContext>();
        var connVM = provider.GetRequiredService<TallyConnectionViewModel>();
        var mainVM = provider.GetRequiredService<MainWindowViewModel>();
        var syncVM = provider.GetRequiredService<SyncViewModel>();

        // Activate
        connVM.SelectedCompany = "RAVI & CO.";
        await connVM.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // Navigate through all sections
        string[] sections = { "Dashboard", "GST", "TDS", "Vouchers", "Ledgers", "Bank", "Reports", "Settings", "Sync" };
        foreach (var sec in sections)
        {
            mainVM.Navigate(sec);
            Assert.Equal("RAVI & CO.", mainVM.ActiveCompany);
        }

        await syncVM.OnNavigatedToAsync();
        Assert.Equal("RAVI & CO.", syncVM.CompanyName);
        Assert.StartsWith("Ready to synchronize RAVI & CO.", syncVM.CurrentTaskDescription);
    }

    [Fact]
    public async Task SaveCompany_RemainsAfterManualRefresh()
    {
        var services = new ServiceCollection();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_manual_refresh_{Guid.NewGuid():N}.db");
        services.AddApplicationServices(config, dbPath);

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync();

        var ctx = provider.GetRequiredService<IActiveCompanyContext>();
        var connVM = provider.GetRequiredService<TallyConnectionViewModel>();
        var mainVM = provider.GetRequiredService<MainWindowViewModel>();

        // Activate
        connVM.SelectedCompany = "RAVI & CO.";
        await connVM.SelectAndSaveCompanyCommand.ExecuteAsync(null);
        Assert.Equal("RAVI & CO.", ctx.ActiveCompanyName);

        // Perform manual scan
        await connVM.ScanForTallyCommand.ExecuteAsync(null);
        Assert.Equal("RAVI & CO.", ctx.ActiveCompanyName);
        Assert.Equal("RAVI & CO.", connVM.ActiveCompany);
        Assert.Equal("RAVI & CO.", mainVM.ActiveCompany);
    }

    [Fact]
    public async Task RealTallyCompany_ReconcilesExistingMockRecord()
    {
        var services = new ServiceCollection();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_reconcile_{Guid.NewGuid():N}.db");
        services.AddApplicationServices(config, dbPath);

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync();

        var auditRepo = provider.GetRequiredService<IAuditRepository>();
        var settingsRepo = provider.GetRequiredService<ISettingsService>();

        // Pre-seed an existing mock record for RAVI & CO.
        await settingsRepo.SetSettingAsync("MockModeEnabled", "false");
        var mockRecord = new Company
        {
            Id = "RAVI & CO.",
            TallyCompanyName = "RAVI & CO.",
            FormalName = "RAVI & CO.",
            IsMock = true,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };
        await auditRepo.EnsureCompanyAsync(mockRecord);

        // Verify pre-seeded state
        var preCheck = await auditRepo.GetCompanyByNameAsync("RAVI & CO.");
        Assert.NotNull(preCheck);
        Assert.True(preCheck.IsMock);

        var ctx = provider.GetRequiredService<IActiveCompanyContext>();
        var connVM = provider.GetRequiredService<TallyConnectionViewModel>();
        var mainVM = provider.GetRequiredService<MainWindowViewModel>();
        var syncVM = provider.GetRequiredService<SyncViewModel>();

        // 1. Discover/Select RAVI & CO.
        connVM.SelectedCompany = "RAVI & CO.";

        // 2. Save & Activate Company
        await connVM.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // 3. Assertions
        Assert.Equal("Verified", connVM.PersistenceStatus);
        Assert.Equal("Verified", connVM.ContextStatus);
        Assert.Equal("RAVI & CO.", ctx.ActiveCompanyName);
        
        var settingVal = await settingsRepo.GetSettingAsync("ActiveCompany", string.Empty);
        Assert.Equal("RAVI & CO.", settingVal);

        var postCheck = await auditRepo.GetCompanyByNameAsync("RAVI & CO.");
        Assert.NotNull(postCheck);
        Assert.False(postCheck.IsMock);
        Assert.True(postCheck.IsActive);

        Assert.Equal("RAVI & CO.", mainVM.ActiveCompany);

        await syncVM.OnNavigatedToAsync();
        Assert.Equal("RAVI & CO.", syncVM.CompanyName);
        Assert.StartsWith("Ready to synchronize RAVI & CO.", syncVM.CurrentTaskDescription);
    }

    [Fact]
    public async Task RealTallyCompany_PersistsAcrossRestart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_restart_reconcile_{Guid.NewGuid():N}.db");
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        // Session 1: Seed real company and activate
        {
            var services1 = new ServiceCollection();
            services1.AddApplicationServices(config, dbPath);
            using var provider1 = services1.BuildServiceProvider();

            var initializer1 = provider1.GetRequiredService<IDatabaseInitializer>();
            await initializer1.InitializeAsync();

            var connVM1 = provider1.GetRequiredService<TallyConnectionViewModel>();
            connVM1.SelectedCompany = "RAVI & CO.";
            await connVM1.SelectAndSaveCompanyCommand.ExecuteAsync(null);

            Assert.Equal("Verified", connVM1.PersistenceStatus);
        }

        // Session 2: New ServiceProvider on same DB
        {
            var services2 = new ServiceCollection();
            services2.AddApplicationServices(config, dbPath);
            using var provider2 = services2.BuildServiceProvider();

            var initializer2 = provider2.GetRequiredService<IDatabaseInitializer>();
            await initializer2.InitializeAsync();

            var ctx2 = provider2.GetRequiredService<IActiveCompanyContext>();
            var company = await ctx2.GetActiveCompanyAsync();

            Assert.NotNull(company);
            Assert.Equal("RAVI & CO.", company.TallyCompanyName);
            Assert.False(company.IsMock);
            Assert.Equal("RAVI & CO.", ctx2.ActiveCompanyName);
        }
    }

    [Fact]
    public async Task SettingsRepository_SetAndGetActiveCompany_ReturnsSameValue()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_settings_repo_{Guid.NewGuid():N}.db");
        var sqliteFactory = new SqliteConnectionFactory(dbPath);
        var initializer = new DatabaseInitializer(sqliteFactory, NullLogger<DatabaseInitializer>.Instance, dbPath);
        await initializer.InitializeAsync();

        var settings = new SettingsRepository(sqliteFactory);

        await settings.SetSettingAsync("ActiveCompany", "SHARED COMPANY");
        var value = await settings.GetSettingAsync("ActiveCompany", string.Empty);

        Assert.Equal("SHARED COMPANY", value);
    }

    [Fact]
    public async Task SetActiveCompanyNameAsync_PersistsAndReadsBackCompany()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_context_persist_{Guid.NewGuid():N}.db");
        var sqliteFactory = new SqliteConnectionFactory(dbPath);
        var initializer = new DatabaseInitializer(sqliteFactory, NullLogger<DatabaseInitializer>.Instance, dbPath);
        await initializer.InitializeAsync();

        var auditRepo = new AuditRepository(sqliteFactory);
        var settingsRepo = new SettingsRepository(sqliteFactory);
        var mockCompanyService = new Mock<ITallyCompanyService>();

        var context = new ActiveCompanyContext(auditRepo, settingsRepo, mockCompanyService.Object);

        await context.SetActiveCompanyNameAsync("SHARED COMPANY");

        Assert.Equal("SHARED COMPANY", context.ActiveCompanyName);
        var persisted = await settingsRepo.GetSettingAsync("ActiveCompany", string.Empty);
        Assert.Equal("SHARED COMPANY", persisted);

        var savedCompany = await auditRepo.GetCompanyByNameAsync("SHARED COMPANY");
        Assert.NotNull(savedCompany);
        Assert.Equal("SHARED COMPANY", savedCompany.TallyCompanyName);
        Assert.False(savedCompany.IsMock);
    }

    [Fact]
    public async Task SettingsRepository_ActiveCompany_WritesAndReadsFromSameDatabase()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_settings_writes_reads_{Guid.NewGuid():N}.db");
        var sqliteFactory = new SqliteConnectionFactory(dbPath);
        var initializer = new DatabaseInitializer(sqliteFactory, NullLogger<DatabaseInitializer>.Instance, dbPath);
        await initializer.InitializeAsync();

        var settings1 = new SettingsRepository(sqliteFactory);

        // 1. Write RAVI & CO.
        await settings1.SetSettingAsync("ActiveCompany", "RAVI & CO.");

        // 2. Read back from connection
        var read1 = await settings1.GetSettingAsync("ActiveCompany");
        Assert.Equal("RAVI & CO.", read1);

        // 3. Overwrite with COMPANY B
        await settings1.SetSettingAsync("ActiveCompany", "COMPANY B");
        var read2 = await settings1.GetSettingAsync("ActiveCompany");
        Assert.Equal("COMPANY B", read2);

        // 4. Read from a completely separate SettingsRepository instance using the SAME database
        var settings2 = new SettingsRepository(new SqliteConnectionFactory(dbPath));
        var readFromInstance2 = await settings2.GetSettingAsync("ActiveCompany");
        Assert.Equal("COMPANY B", readFromInstance2);
    }

    [Fact]
    public async Task ActiveCompanyContext_RealSQLite_PersistsAcrossContextInstances()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_context_instances_{Guid.NewGuid():N}.db");
        var sqliteFactory = new SqliteConnectionFactory(dbPath);
        var initializer = new DatabaseInitializer(sqliteFactory, NullLogger<DatabaseInitializer>.Instance, dbPath);
        await initializer.InitializeAsync();

        var auditRepo1 = new AuditRepository(sqliteFactory);
        var settingsRepo1 = new SettingsRepository(sqliteFactory);
        var mockCompanyService = new Mock<ITallyCompanyService>();

        // Context #1: Set Active Company
        var ctx1 = new ActiveCompanyContext(auditRepo1, settingsRepo1, mockCompanyService.Object);
        await ctx1.SetActiveCompanyNameAsync("RAVI & CO.");

        Assert.Equal("RAVI & CO.", ctx1.ActiveCompanyName);

        // Context #2: Separate instance pointing to same database
        var auditRepo2 = new AuditRepository(new SqliteConnectionFactory(dbPath));
        var settingsRepo2 = new SettingsRepository(new SqliteConnectionFactory(dbPath));
        var ctx2 = new ActiveCompanyContext(auditRepo2, settingsRepo2, mockCompanyService.Object);

        var restoredCompany = await ctx2.GetActiveCompanyAsync();
        Assert.NotNull(restoredCompany);
        Assert.Equal("RAVI & CO.", restoredCompany.TallyCompanyName);
        Assert.Equal("RAVI & CO.", ctx2.ActiveCompanyName);

        // Verify period & settings persisted
        var activeCompanySetting = await settingsRepo2.GetSettingAsync("ActiveCompany");
        Assert.Equal("RAVI & CO.", activeCompanySetting);

        var fySetting = await settingsRepo2.GetSettingAsync("FinancialYear");
        Assert.False(string.IsNullOrEmpty(fySetting));

        var fromSetting = await settingsRepo2.GetSettingAsync("AuditPeriodFrom");
        Assert.False(string.IsNullOrEmpty(fromSetting));

        var toSetting = await settingsRepo2.GetSettingAsync("AuditPeriodTo");
        Assert.False(string.IsNullOrEmpty(toSetting));
    }

    [Fact]
    public async Task SaveCompany_CannotBeClearedByStartupInitialization()
    {
        var services = new ServiceCollection();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_startup_init_{Guid.NewGuid():N}.db");
        services.AddApplicationServices(config, dbPath);

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync();

        var ctx = provider.GetRequiredService<IActiveCompanyContext>();
        var connVM = provider.GetRequiredService<TallyConnectionViewModel>();
        var settings = provider.GetRequiredService<ISettingsService>();

        // 1. Commit company
        connVM.SelectedCompany = "RAVI & CO.";
        await connVM.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // 2. Simulate startup initialization running in background
        await ctx.EnsureAndInitializeActiveCompanyAsync();

        // 3. Verify ActiveCompany was NOT wiped to empty
        Assert.Equal("RAVI & CO.", ctx.ActiveCompanyName);
        var persisted = await settings.GetSettingAsync("ActiveCompany");
        Assert.Equal("RAVI & CO.", persisted);
    }

    [Fact]
    public async Task TemporaryTallyDiscoveryFailure_DoesNotClearCommittedCompany()
    {
        var services = new ServiceCollection();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_tally_fail_{Guid.NewGuid():N}.db");
        services.AddApplicationServices(config, dbPath);

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync();

        var ctx = provider.GetRequiredService<IActiveCompanyContext>();
        var connVM = provider.GetRequiredService<TallyConnectionViewModel>();
        var settings = provider.GetRequiredService<ISettingsService>();

        // 1. Commit company
        connVM.SelectedCompany = "RAVI & CO.";
        await connVM.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // 2. Scan Tally when Tally is offline / scanning ports that fail
        await connVM.ScanForTallyCommand.ExecuteAsync(null);

        // 3. Verify committed company remains active and persisted
        Assert.Equal("RAVI & CO.", ctx.ActiveCompanyName);
        Assert.Equal("RAVI & CO.", connVM.ActiveCompany);
        var persisted = await settings.GetSettingAsync("ActiveCompany");
        Assert.Equal("RAVI & CO.", persisted);
    }

    [Fact]
    public async Task SettingsWriteRead_RoundTrip()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_roundtrip_{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(dbPath);
        var init = new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance, dbPath);
        await init.InitializeAsync();

        var settings = new SettingsRepository(factory);
        await settings.SetSettingAsync("ActiveCompany", "ACME CORP");
        var val = await settings.GetSettingAsync("ActiveCompany");
        Assert.Equal("ACME CORP", val);
    }

    [Fact]
    public async Task CompanyChange_NoStaleCompanyRemains()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_change_{Guid.NewGuid():N}.db");
        var sqliteFactory = new SqliteConnectionFactory(dbPath);
        var initializer = new DatabaseInitializer(sqliteFactory, NullLogger<DatabaseInitializer>.Instance, dbPath);
        await initializer.InitializeAsync();

        var auditRepo = new AuditRepository(sqliteFactory);
        var settingsRepo = new SettingsRepository(sqliteFactory);
        var mockCompanyService = new Mock<ITallyCompanyService>();
        var ctx = new ActiveCompanyContext(auditRepo, settingsRepo, mockCompanyService.Object);
        var syncVM = new SyncViewModel(new Mock<ISyncManager>().Object, mockCompanyService.Object, settingsRepo, ctx);

        // 1. Activate Company A
        await ctx.SetActiveCompanyNameAsync("Company A");
        await syncVM.OnNavigatedToAsync();
        Assert.Equal("Company A", syncVM.CompanyName);

        // 2. Activate Company B
        await ctx.SetActiveCompanyNameAsync("Company B");
        await syncVM.OnNavigatedToAsync();
        Assert.Equal("Company B", syncVM.CompanyName);
        Assert.Equal("Company B", ctx.ActiveCompanyName);
        Assert.DoesNotContain("Company A", syncVM.CompanyName);
    }
}
