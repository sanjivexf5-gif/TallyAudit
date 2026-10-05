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

        var prevEnv = System.Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        try
        {
            System.Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);

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
        finally
        {
            System.Environment.SetEnvironmentVariable("GEMINI_API_KEY", prevEnv);
        }
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

public class AuditAssistantServiceTests
{
    [Fact]
    public async Task GenerateRiskSummaryAsync_WithNullSummary_ReturnsHelpfulMessage()
    {
        var mockProvider = new Mock<IAuditAiProvider>();
        var service = new AuditAssistantService(mockProvider.Object);

        var result = await service.GenerateRiskSummaryAsync(null!);

        Assert.NotNull(result);
        Assert.Contains("No audit dashboard summary available", result);
        mockProvider.Verify(p => p.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateRiskSummaryAsync_WithValidSummary_InvokesProviderWithDeterministicSummary()
    {
        var mockProvider = new Mock<IAuditAiProvider>();
        mockProvider
            .Setup(p => p.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Mock Risk Summary Response");

        var service = new AuditAssistantService(mockProvider.Object);

        var summary = new Core.Domain.Audit.AuditDashboardSummary
        {
            CompanyName = "Acme Corp Ltd",
            FinancialYear = "FY 2025-26",
            OverallRiskScore = 78,
            OverallRiskLevel = "High",
            TotalFindings = 15,
            CriticalCount = 2,
            HighCount = 5,
            MediumCount = 6,
            LowCount = 2,
            TotalExceptionAmount = 500000m,
            AuditAreaSummaries = new[]
            {
                new Core.Domain.Audit.AuditAreaSummary
                {
                    AreaName = "GST Audit",
                    FindingCount = 5,
                    RiskScore = 35,
                    ExceptionAmount = 250000m
                }
            }
        };

        var result = await service.GenerateRiskSummaryAsync(summary);

        Assert.Equal("Mock Risk Summary Response", result);
        mockProvider.Verify(p => p.GenerateTextAsync(
            It.Is<string>(sys => sys.Contains("executive audit risk analyst")),
            It.Is<string>(user => user.Contains("Acme Corp Ltd") && user.Contains("78 / 100") && user.Contains("GST Audit")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SuggestInvestigationFocusAsync_WithNullSummary_ReturnsHelpfulMessage()
    {
        var mockProvider = new Mock<IAuditAiProvider>();
        var service = new AuditAssistantService(mockProvider.Object);

        var result = await service.SuggestInvestigationFocusAsync(null!);

        Assert.NotNull(result);
        Assert.Contains("No audit dashboard summary available", result);
        mockProvider.Verify(p => p.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SuggestInvestigationFocusAsync_WithValidSummary_InvokesProviderWithDeterministicData()
    {
        var mockProvider = new Mock<IAuditAiProvider>();
        mockProvider
            .Setup(p => p.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Mock Investigation Guidance");

        var service = new AuditAssistantService(mockProvider.Object);

        var summary = new Core.Domain.Audit.AuditDashboardSummary
        {
            CompanyName = "Acme Corp Ltd",
            FinancialYear = "FY 2025-26",
            OverallRiskScore = 65,
            OverallRiskLevel = "Medium",
            OpenFindings = 10,
            TotalExceptionAmount = 150000m,
            TopFindings = new[]
            {
                new Core.Domain.Audit.TopFindingItem
                {
                    Title = "Missing GSTR-2B ITC Match",
                    Category = "GST",
                    Severity = Core.Domain.Audit.SeverityLevel.High,
                    Amount = 75000m,
                    RecommendedAuditProcedure = "Verify vendor invoice on GST portal"
                }
            }
        };

        var result = await service.SuggestInvestigationFocusAsync(summary);

        Assert.Equal("Mock Investigation Guidance", result);
        mockProvider.Verify(p => p.GenerateTextAsync(
            It.Is<string>(sys => sys.Contains("investigation focus")),
            It.Is<string>(user => user.Contains("Missing GSTR-2B ITC Match") && user.Contains("Acme Corp Ltd")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

