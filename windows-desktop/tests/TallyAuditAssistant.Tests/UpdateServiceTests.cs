using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Common;
using TallyAuditAssistant.Engine.Services;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class UpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdates_ReturnsCurrentAppVersion()
    {
        var service = new UpdateService(NullLogger<UpdateService>.Instance);
        var update = await service.CheckForUpdatesAsync();

        Assert.NotNull(update);
        Assert.Equal(AppVersion.Version, update.CurrentVersion);
        Assert.False(update.IsUpdateAvailable);
    }

    [Fact]
    public void GetCurrentVersion_ReturnsVersionString()
    {
        var service = new UpdateService(NullLogger<UpdateService>.Instance);
        Assert.Equal("1.0.0", service.GetCurrentVersion());
    }
}
