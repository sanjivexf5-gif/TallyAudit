using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Engine.Services;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class TallyDrillDownServiceTests
{
    private readonly Mock<ISqliteConnectionFactory> _factoryMock;
    private readonly Mock<ITallyClient> _tallyClientMock;
    private readonly TallyDrillDownService _service;

    public TallyDrillDownServiceTests()
    {
        _factoryMock = new Mock<ISqliteConnectionFactory>();
        _tallyClientMock = new Mock<ITallyClient>();
        _service = new TallyDrillDownService(
            _factoryMock.Object, 
            _tallyClientMock.Object, 
            NullLogger<TallyDrillDownService>.Instance);
    }

    [Fact]
    public void GenerateNavigationGuide_Produces_Accurate_Tally_Shortcuts()
    {
        var date = new DateTime(2025, 6, 5);
        var guide = _service.GenerateNavigationGuide(
            "Apex Industrial Solutions Pvt Ltd",
            "PUR-05",
            "Purchase",
            date,
            "10042");

        Assert.Contains("PUR-05", guide.GatewayPath);
        Assert.Contains("05-Jun-2025", guide.GatewayPath);
        Assert.Contains("Alt+G", guide.KeyboardShortcuts);
        Assert.Contains("10042", guide.TdlXmlPayload);
    }

    [Fact]
    public async Task AttemptOpenInTally_Handles_Offline_Tally_Server_Gracefully()
    {
        _tallyClientMock
            .Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.AttemptOpenInTallyAsync("COMP-01", "PUR-05");

        Assert.False(result.IsDirectLaunchSuccess);
        Assert.True(result.RequiresManualNavigationFallback);
        Assert.Contains("Port 9000", result.StatusMessage);
    }
}
