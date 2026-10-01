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
