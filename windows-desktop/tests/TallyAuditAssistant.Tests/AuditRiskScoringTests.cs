using TallyAuditAssistant.Core.Domain.Audit;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class AuditRiskScoringTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, 0, "Low")]
    [InlineData(1, 0, 0, 0, 10, "Low")]
    [InlineData(0, 5, 0, 0, 30, "Medium")]
    [InlineData(0, 0, 9, 0, 27, "Medium")]
    [InlineData(0, 0, 0, 25, 25, "Medium")]
    [InlineData(8, 0, 0, 0, 80, "Critical")]
    public void CalculateScore_UsesSharedWeightsAndThresholds(
        int critical, int high, int medium, int low, int expectedScore, string expectedLabel)
    {
        var options = new RiskScoringOptions();

        var score = AuditRiskScoring.CalculateScore(critical, high, medium, low, options);

        Assert.Equal(expectedScore, score);
        Assert.Equal(expectedLabel, AuditRiskScoring.GetRiskLevel(score, options));
    }

    [Fact]
    public void CalculateScore_ClampsScoreToZeroThroughOneHundred()
    {
        var options = new RiskScoringOptions { BenchmarkMaxPoints = 0 };

        Assert.Equal(100, AuditRiskScoring.CalculateScore(100, 0, 0, 0, options));
        Assert.Equal(0, AuditRiskScoring.CalculateScore(-10, 0, 0, 0, options));
    }

    [Fact]
    public void CalculateScore_RespectsCustomWeightsAndBenchmark()
    {
        var options = new RiskScoringOptions
        {
            CriticalWeight = 20,
            HighWeight = 10,
            MediumWeight = 5,
            LowWeight = 1,
            BenchmarkMaxPoints = 200
        };

        Assert.Equal(10, AuditRiskScoring.CalculateScore(1, 0, 0, 0, options));
    }
}
