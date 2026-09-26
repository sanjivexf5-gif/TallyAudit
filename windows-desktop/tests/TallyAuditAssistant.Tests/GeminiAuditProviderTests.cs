using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Engine.Ai;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class GeminiAuditProviderTests
{
    [Fact]
    public async Task GenerateTextAsync_WhenNoApiKeyConfigured_ReturnsHelpfulMessageWithoutCrashing()
    {
        // Arrange
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockSettingsService = new Mock<ISettingsService>();
        
        mockSettingsService
            .Setup(s => s.GetSettingAsync("GeminiApiKey", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var provider = new GeminiAuditProvider(
            mockHttpClientFactory.Object,
            mockSettingsService.Object,
            NullLogger<GeminiAuditProvider>.Instance);

        // Act
        var result = await provider.GenerateTextAsync("You are an auditor.", "Explain rule GST-01");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("AI assistance is not configured", result);
        Assert.False(provider.IsConfigured);
    }

    [Fact]
    public async Task GenerateTextAsync_WhenCancelled_ReturnsCancellationNotice()
    {
        // Arrange
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockSettingsService = new Mock<ISettingsService>();

        mockSettingsService
            .Setup(s => s.GetSettingAsync("GeminiApiKey", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-dummy-api-key");

        var provider = new GeminiAuditProvider(
            mockHttpClientFactory.Object,
            mockSettingsService.Object,
            NullLogger<GeminiAuditProvider>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token

        // Act
        var result = await provider.GenerateTextAsync("System", "User prompt", cts.Token);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("cancelled", result, System.StringComparison.OrdinalIgnoreCase);
    }
}
