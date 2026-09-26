using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Core.Licensing;
using TallyAuditAssistant.Engine.Services;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class LicensingServiceTests
{
    private readonly Mock<ISecureStorage> _mockStorage = new();
    private readonly Mock<IAuditTrailService> _mockAuditTrail = new();

    [Fact]
    public async Task GetCurrentLicense_WhenNoTokenExists_ReturnsUnactivatedTrial()
    {
        _mockStorage.Setup(s => s.GetSecret(It.IsAny<string>()))
            .Returns(string.Empty);

        var service = new LicenseService(_mockStorage.Object, _mockAuditTrail.Object, NullLogger<LicenseService>.Instance);
        var license = await service.GetCurrentLicenseAsync();

        Assert.NotNull(license);
        Assert.Equal(LicenseStatus.NotActivated, license.Status);
        Assert.False(service.CanUseFeature("ExportReports"));
    }

    [Fact]
    public async Task ActivateLicense_WithValidProKey_ActivatesProEdition()
    {
        var service = new LicenseService(_mockStorage.Object, _mockAuditTrail.Object, NullLogger<LicenseService>.Instance);
        var result = await service.ActivateLicenseAsync("TAA-2026-PRO-1234-5678");

        Assert.True(result.Success);
        Assert.NotNull(result.License);
        Assert.Equal(LicenseStatus.Active, result.License.Status);
        Assert.Equal(LicenseType.Professional, result.License.LicenseType);
        Assert.True(service.CanUseFeature("GstAudit"));
        Assert.True(service.CanUseFeature("ExportReports"));
    }

    [Fact]
    public async Task StartTrial_Creates14DayEvaluation()
    {
        var service = new LicenseService(_mockStorage.Object, _mockAuditTrail.Object, NullLogger<LicenseService>.Instance);
        var result = await service.StartTrialAsync("Acme CA Audit", "auditor@acme.com");

        Assert.True(result.Success);
        Assert.NotNull(result.License);
        Assert.Equal(LicenseStatus.Trial, result.License.Status);
        Assert.Equal(14, result.License.RemainingTrialDays);
    }
}
