using Moq;
using TallyAuditAssistant.App.Services;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Tests;

public sealed class AutomationReadinessServiceTests : IDisposable
{
    private readonly Mock<ITallyConnection> _connection = new();
    private readonly Mock<IActiveCompanyContext> _companyContext = new();
    private readonly Mock<IApplicationDataPathService> _dataPaths = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"TallyAuditReadiness-{Guid.NewGuid():N}");

    public AutomationReadinessServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _dataPaths.SetupGet(x => x.ApplicationDataDirectory).Returns(_directory);
        _dataPaths.SetupGet(x => x.DatabasePath).Returns(Path.Combine(_directory, "audit.db"));
        _dataPaths.SetupGet(x => x.LogsDirectory).Returns(Path.Combine(_directory, "Logs"));

        _connection.SetupGet(x => x.ActiveEndpoint)
            .Returns(new TallyEndpointInfo("localhost", 9000, true));
        _connection.Setup(x => x.TestConnectionDetailedAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TallyEndpointInfo("localhost", 9000, true));
        _connection.Setup(x => x.DiscoverTallyAsync(
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TallyEndpointInfo("localhost", 9000, true));
        _connection.SetupGet(x => x.LastErrorMessage).Returns((string?)null);

        _companyContext.Setup(x => x.GetActiveCompanyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Company
            {
                Id = "company-1",
                TallyCompanyName = "Test Company",
                BooksFromDate = new DateTime(2025, 4, 1)
            });
        _companyContext.Setup(x => x.GetActivePeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinancialPeriod
            {
                CompanyId = "company-1",
                StartDate = new DateTime(2025, 4, 1),
                EndDate = new DateTime(2026, 3, 31)
            });
    }

    [Fact]
    public async Task CheckAsync_WhenPrerequisitesAreAvailable_ReturnsReadyAndPassDetails()
    {
        var result = await CreateService().CheckAsync();

        Assert.True(result.IsReady);
        Assert.Contains("READY", result.Report);
        Assert.Contains("[PASS] TallyPrime is responding", result.Report);
        Assert.Contains("[PASS] Active company is selected: Test Company", result.Report);
        Assert.Contains("[PASS] Audit period is valid", result.Report);
        Assert.Contains("[PASS] Local application storage is writable", result.Report);
        Assert.Empty(Directory.GetFiles(_directory, ".automation-readiness-*.tmp"));
    }

    [Fact]
    public async Task CheckAsync_WhenTallyIsUnavailable_ReturnsNotReadyWithConnectionGuidance()
    {
        _connection.Setup(x => x.TestConnectionDetailedAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TallyEndpointInfo("localhost", 9000, false, ErrorMessage: "Connection refused"));
        _connection.Setup(x => x.DiscoverTallyAsync(
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TallyEndpointInfo?)null);

        var result = await CreateService().CheckAsync();

        Assert.False(result.IsReady);
        Assert.Contains("[FAIL] TallyPrime is not responding", result.Report);
        Assert.Contains("Connection refused", result.Report);
    }

    [Fact]
    public async Task CheckAsync_WhenNoCompanyIsSelected_ReturnsNotReady()
    {
        _companyContext.Setup(x => x.GetActiveCompanyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Company?)null);

        var result = await CreateService().CheckAsync();

        Assert.False(result.IsReady);
        Assert.Contains("[FAIL] No active TallyPrime company is selected", result.Report);
        Assert.Contains("[FAIL] Audit period cannot be validated", result.Report);
    }

    [Fact]
    public async Task CheckAsync_WhenAuditPeriodIsInverted_ReturnsNotReady()
    {
        _companyContext.Setup(x => x.GetActivePeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinancialPeriod
            {
                CompanyId = "company-1",
                StartDate = new DateTime(2026, 4, 1),
                EndDate = new DateTime(2025, 4, 1)
            });

        var result = await CreateService().CheckAsync();

        Assert.False(result.IsReady);
        Assert.Contains("[FAIL] Audit period is invalid", result.Report);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private AutomationReadinessService CreateService() =>
        new(_connection.Object, _companyContext.Object, _dataPaths.Object);
}
