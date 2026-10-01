using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.App.ViewModels;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Core.Services;
using TallyAuditAssistant.TallyIntegration;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class TallyConnectionViewModelTests
{
    private readonly Mock<ITallyConnection> _mockConnection;
    private readonly Mock<ITallyCompanyService> _mockCompanyService;
    private readonly Mock<ISettingsService> _mockSettings;
    private readonly TallyConnectionMonitor _monitor;
    private readonly Mock<IActiveCompanyContext> _mockContext;
    private readonly Mock<ITallyMasterService> _mockMasterService;
    private readonly Mock<ITallyVoucherService> _mockVoucherService;

    public TallyConnectionViewModelTests()
    {
        _mockConnection = new Mock<ITallyConnection>();
        _mockCompanyService = new Mock<ITallyCompanyService>();
        _mockSettings = new Mock<ISettingsService>();
        _mockMasterService = new Mock<ITallyMasterService>();
        _mockVoucherService = new Mock<ITallyVoucherService>();

        _mockSettings.Setup(s => s.GetTallyHostAsync()).ReturnsAsync("localhost");
        _mockSettings.Setup(s => s.GetTallyPortAsync()).ReturnsAsync(9000);

        _monitor = new TallyConnectionMonitor(_mockConnection.Object, _mockSettings.Object, NullLogger<TallyConnectionMonitor>.Instance);
        _mockContext = new Mock<IActiveCompanyContext>();
    }

    [Fact]
    public async Task ScanForTallyAsync_WhenEndpointResponsive_PopulatesAvailableCompanies()
    {
        var endpoint = new TallyEndpointInfo(
            Host: "localhost",
            Port: 9000,
            IsResponsive: true,
            ServerVersion: "TallyPrime 4.0",
            ActiveCompany: null,
            LatencyMs: 2,
            ErrorMessage: null,
            FailureCause: ConnectionFailureCause.None
        );

        _mockConnection.Setup(c => c.CheckIfProcessRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockConnection.Setup(c => c.DiscoverTallyAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint);

        var companyList = new List<string> { "Test Company A", "Test Company B" };
        _mockCompanyService.Setup(s => s.GetOpenCompaniesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(companyList);

        var vm = new TallyConnectionViewModel(
            _mockConnection.Object,
            _mockCompanyService.Object,
            _mockSettings.Object,
            _monitor,
            _mockContext.Object,
            _mockMasterService.Object,
            _mockVoucherService.Object,
            NullLogger<TallyConnectionViewModel>.Instance
        );

        await vm.ScanForTallyCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.Equal(2, vm.AvailableCompanies.Count);
        Assert.Contains("Test Company A", vm.AvailableCompanies);
        Assert.Contains("Test Company B", vm.AvailableCompanies);
        
        // New UX: First discovered company is selected in dropdown. 
        // Programmatic update during scan does NOT auto-commit.
        Assert.Equal("Test Company A", vm.SelectedCompany);
        Assert.Equal("—", vm.ActiveCompany);
        _mockContext.Verify(c => c.SetActiveCompanyNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // User interaction: Changing selection in ComboBox does NOT commit it
        vm.SelectedCompany = "Test Company B";
        Assert.Equal("Test Company B", vm.SelectedCompany);
        Assert.Equal("—", vm.ActiveCompany);
        _mockContext.Verify(c => c.SetActiveCompanyNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // Explicit Save & Activate commits the selected company
        await vm.SelectAndSaveCompanyCommand.ExecuteAsync(null);
        Assert.Equal("Test Company B", vm.ActiveCompany);
        _mockContext.Verify(c => c.SetActiveCompanyNameAsync("Test Company B", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(string.Empty, vm.ValidationMessage);
        Assert.Contains("✓ Company query completed", vm.DiagnosticReport);
    }

    [Fact]
    public async Task TestManualConnectionAsync_WhenSuccessful_PopulatesAvailableCompanies()
    {
        var result = new TallyEndpointInfo(
            Host: "localhost",
            Port: 9000,
            IsResponsive: true,
            ServerVersion: "TallyPrime",
            ActiveCompany: null,
            LatencyMs: 5,
            ErrorMessage: null,
            FailureCause: ConnectionFailureCause.None
        );

        _mockConnection.Setup(c => c.TestConnectionDetailedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var companyList = new List<string> { "Apex Solutions" };
        _mockCompanyService.Setup(s => s.GetOpenCompaniesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(companyList);

        var vm = new TallyConnectionViewModel(
            _mockConnection.Object,
            _mockCompanyService.Object,
            _mockSettings.Object,
            _monitor,
            _mockContext.Object,
            _mockMasterService.Object,
            _mockVoucherService.Object,
            NullLogger<TallyConnectionViewModel>.Instance
        );

        await vm.TestManualConnectionCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.Single(vm.AvailableCompanies);
        Assert.Equal("Apex Solutions", vm.SelectedCompany);
        Assert.Contains("✓ Manual connection verified", vm.DiagnosticReport);
    }

    [Fact]
    public async Task ScanForTallyAsync_WhenNoCompaniesOpen_LeavesListEmptyGracefully()
    {
        var endpoint = new TallyEndpointInfo(
            Host: "localhost",
            Port: 9000,
            IsResponsive: true,
            ServerVersion: "TallyPrime",
            ActiveCompany: null,
            LatencyMs: 2,
            ErrorMessage: null,
            FailureCause: ConnectionFailureCause.None
        );

        _mockConnection.Setup(c => c.DiscoverTallyAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint);

        _mockCompanyService.Setup(s => s.GetOpenCompaniesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var vm = new TallyConnectionViewModel(
            _mockConnection.Object,
            _mockCompanyService.Object,
            _mockSettings.Object,
            _monitor,
            _mockContext.Object,
            _mockMasterService.Object,
            _mockVoucherService.Object,
            NullLogger<TallyConnectionViewModel>.Instance
        );

        await vm.ScanForTallyCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.Empty(vm.AvailableCompanies);
        Assert.Null(vm.SelectedCompany);
        Assert.Contains("⚠ TallyPrime responded successfully, but no loaded company was returned.", vm.DiagnosticReport);
    }

    [Fact]
    public async Task ScanForTallyAsync_WhenCompanyQueryThrowsException_ReportsCorrectDiagnostic()
    {
        var endpoint = new TallyEndpointInfo(
            Host: "localhost",
            Port: 9000,
            IsResponsive: true,
            ServerVersion: "TallyPrime",
            ActiveCompany: null,
            LatencyMs: 2,
            ErrorMessage: null,
            FailureCause: ConnectionFailureCause.None
        );

        _mockConnection.Setup(c => c.DiscoverTallyAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint);

        _mockCompanyService.Setup(s => s.GetOpenCompaniesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Internal XML Parser Error"));

        var vm = new TallyConnectionViewModel(
            _mockConnection.Object,
            _mockCompanyService.Object,
            _mockSettings.Object,
            _monitor,
            _mockContext.Object,
            _mockMasterService.Object,
            _mockVoucherService.Object,
            NullLogger<TallyConnectionViewModel>.Instance
        );

        await vm.ScanForTallyCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.Empty(vm.AvailableCompanies);
        Assert.Contains("✗ Connected to TallyPrime, but the company list could not be read.", vm.DiagnosticReport);
        Assert.Contains("Error: Internal XML Parser Error", vm.DiagnosticReport);
    }

    [Fact]
    public async Task OnSelectedCompanyChanged_RapidSelection_DoesNotCommitUntilSaved()
    {
        var profile1 = new TallyCompanyProfile
        {
            Name = "Company 1",
            GSTIN = "GST-1",
            StateName = "State 1",
            BooksBeginningFrom = new DateTime(2025, 4, 1)
        };

        var profile2 = new TallyCompanyProfile
        {
            Name = "Company 2",
            GSTIN = "GST-2",
            StateName = "State 2",
            BooksBeginningFrom = new DateTime(2026, 4, 1)
        };

        _mockCompanyService.Setup(s => s.GetCompanyProfileTypedAsync("Company 1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile1);

        _mockCompanyService.Setup(s => s.GetCompanyProfileTypedAsync("Company 2", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile2);

        var vm = new TallyConnectionViewModel(
            _mockConnection.Object,
            _mockCompanyService.Object,
            _mockSettings.Object,
            _monitor,
            _mockContext.Object,
            _mockMasterService.Object,
            _mockVoucherService.Object,
            NullLogger<TallyConnectionViewModel>.Instance
        );

        // Fast sequential selections (dropdown changes)
        // These simulate user clicks which should NOT trigger commits
        vm.SelectedCompany = "Company 1";
        vm.SelectedCompany = "Company 2";

        // Dropdown reflects Company 2, but ActiveCompany remains uncommitted
        Assert.Equal("Company 2", vm.SelectedCompany);
        Assert.Equal("—", vm.ActiveCompany);
        _mockContext.Verify(c => c.SetActiveCompanyNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // User clicks Save & Activate
        await vm.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        // Target state must reflect Company 2
        Assert.Equal("Company 2", vm.ActiveCompany);
        Assert.Equal("GST-2", vm.CompanyGstin);
        Assert.Equal("State 2", vm.CompanyState);
        _mockContext.Verify(c => c.SetActiveCompanyNameAsync("Company 2", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(string.Empty, vm.ValidationMessage);
    }

    [Fact]
    public async Task SelectAndSaveCompany_WhenEmptySelection_SetsValidationMessage()
    {
        var vm = new TallyConnectionViewModel(
            _mockConnection.Object,
            _mockCompanyService.Object,
            _mockSettings.Object,
            _monitor,
            _mockContext.Object,
            _mockMasterService.Object,
            _mockVoucherService.Object,
            NullLogger<TallyConnectionViewModel>.Instance
        );

        vm.SelectedCompany = null;
        await vm.SelectAndSaveCompanyCommand.ExecuteAsync(null);

        Assert.Equal("Please select a valid company from the dropdown before committing.", vm.ValidationMessage);
        Assert.Equal("—", vm.ActiveCompany);
        _mockContext.Verify(c => c.SetActiveCompanyNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // Selecting a company must immediately clear the validation message
        vm.SelectedCompany = "RAVI & CO.";
        Assert.Equal(string.Empty, vm.ValidationMessage);
    }

    [Fact]
    public async Task SelectAndSaveCompany_WhenCalledConcurrently_PreventsDuplicateCommit()
    {
        _mockCompanyService.Setup(s => s.GetCompanyProfileTypedAsync("RAVI & CO.", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(50);
                return new TallyCompanyProfile { Name = "RAVI & CO.", BooksBeginningFrom = new DateTime(2025, 4, 1) };
            });

        var vm = new TallyConnectionViewModel(
            _mockConnection.Object,
            _mockCompanyService.Object,
            _mockSettings.Object,
            _monitor,
            _mockContext.Object,
            _mockMasterService.Object,
            _mockVoucherService.Object,
            NullLogger<TallyConnectionViewModel>.Instance
        );

        vm.SelectedCompany = "RAVI & CO.";

        var task1 = vm.SelectAndSaveCompanyCommand.ExecuteAsync(null);
        var task2 = vm.SelectAndSaveCompanyCommand.ExecuteAsync(null);
        await Task.WhenAll(task1, task2);

        _mockContext.Verify(c => c.SetActiveCompanyNameAsync("RAVI & CO.", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("RAVI & CO.", vm.ActiveCompany);
        Assert.Equal(string.Empty, vm.ValidationMessage);
    }

    [Fact]
    public async Task ActiveCompanyContext_IsSharedAsSingleton()
    {
        // This test verifies the design requirement that IActiveCompanyContext is shared
        var context = new ActiveCompanyContext(new Mock<IAuditRepository>().Object, new Mock<ISettingsService>().Object, new Mock<ITallyCompanyService>().Object);
        
        var connVM = new TallyConnectionViewModel(
            _mockConnection.Object, _mockCompanyService.Object, _mockSettings.Object, _monitor, context, _mockMasterService.Object, _mockVoucherService.Object);
            
        var syncVM = new SyncViewModel(new Mock<ISyncManager>().Object, _mockCompanyService.Object, _mockSettings.Object, context);
        
        bool syncNotified = false;
        syncVM.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(syncVM.CompanyName)) syncNotified = true; };
        
        // Act: Change company in Connection VM
        await context.SetActiveCompanyNameAsync("SHARED COMPANY");
        
        // Assert: Both see the same state
        Assert.Equal("SHARED COMPANY", connVM.ActiveCompany);
        Assert.Equal("SHARED COMPANY", syncVM.CompanyName);
    }
}
