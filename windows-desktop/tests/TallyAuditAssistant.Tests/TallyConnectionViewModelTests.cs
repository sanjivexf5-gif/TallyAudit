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

    public TallyConnectionViewModelTests()
    {
        _mockConnection = new Mock<ITallyConnection>();
        _mockCompanyService = new Mock<ITallyCompanyService>();
        _mockSettings = new Mock<ISettingsService>();

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

        _mockConnection.Setup(c => c.CheckIfProcessRunningAsync()).ReturnsAsync(true);
        _mockConnection.Setup(c => c.DiscoverTallyAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint);

        var companyList = new List<string> { "Test Company A", "Test Company B" };
        _mockCompanyService.Setup(s => s.GetOpenCompaniesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(companyList);

        var vm = new TallyConnectionViewModel(
            _mockConnection.Object,
            _mockCompanyService.Object,
            _mockSettings.Object,
            _monitor,
            _mockContext.Object,
            NullLogger<TallyConnectionViewModel>.Instance
        );

        await vm.ScanForTallyCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.Equal(2, vm.AvailableCompanies.Count);
        Assert.Contains("Test Company A", vm.AvailableCompanies);
        Assert.Contains("Test Company B", vm.AvailableCompanies);
        Assert.Equal("Test Company A", vm.SelectedCompany);
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
            NullLogger<TallyConnectionViewModel>.Instance
        );

        await vm.ScanForTallyCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.Empty(vm.AvailableCompanies);
        Assert.Null(vm.SelectedCompany);
        Assert.Contains("⚠ TallyPrime responded successfully, but no open companies were returned", vm.DiagnosticReport);
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
            NullLogger<TallyConnectionViewModel>.Instance
        );

        await vm.ScanForTallyCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.Empty(vm.AvailableCompanies);
        Assert.Contains("✗ Tally company query failed: Internal XML Parser Error", vm.DiagnosticReport);
    }

    [Fact]
    public async Task OnSelectedCompanyChanged_RapidSelection_GuardsAgainstStaleDataRace()
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

        // Delay company 1 load to simulate a slow network call that finishes late
        _mockCompanyService.Setup(s => s.GetCompanyProfileTypedAsync("Company 1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(100);
                return profile1;
            });

        _mockCompanyService.Setup(s => s.GetCompanyProfileTypedAsync("Company 2", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile2);

        var vm = new TallyConnectionViewModel(
            _mockConnection.Object,
            _mockCompanyService.Object,
            _mockSettings.Object,
            _monitor,
            _mockContext.Object,
            NullLogger<TallyConnectionViewModel>.Instance
        );

        // Fast sequential selections
        vm.SelectedCompany = "Company 1";
        await Task.Delay(10);
        vm.SelectedCompany = "Company 2";

        // Wait for slow load to complete
        await Task.Delay(150);

        // Target state must reflect Company 2, never overwritten by slow Company 1
        Assert.Equal("Company 2", vm.ActiveCompany);
        Assert.Equal("GST-2", vm.CompanyGstin);
        Assert.Equal("State 2", vm.CompanyState);
    }
}
