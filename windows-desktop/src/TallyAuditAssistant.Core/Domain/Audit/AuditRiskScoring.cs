using System;

namespace TallyAuditAssistant.Core.Domain.Audit;

/// <summary>
/// Shared, deterministic risk scoring used by audit dashboards.
/// Scores are normalized to 0-100 using the configured benchmark and weights.
/// </summary>
public static class AuditRiskScoring
{
    public static int CalculateScore(
        int criticalCount,
        int highCount,
        int mediumCount,
        int lowCount,
        RiskScoringOptions? options = null)
    {
        options ??= new RiskScoringOptions();

        var rawPoints =
            (long)Math.Max(0, criticalCount) * options.CriticalWeight +
            (long)Math.Max(0, highCount) * options.HighWeight +
            (long)Math.Max(0, mediumCount) * options.MediumWeight +
            (long)Math.Max(0, lowCount) * options.LowWeight;

        var benchmark = Math.Max(1, options.BenchmarkMaxPoints);
        var normalized = Math.Round((decimal)rawPoints * 100m / benchmark);
        if (normalized <= 0m) return 0;
        if (normalized >= 100m) return 100;
        return (int)normalized;
    }

    public static string GetRiskLevel(int score, RiskScoringOptions? options = null)
    {
        options ??= new RiskScoringOptions();

        if (score <= options.LowMaxThreshold) return "Low";
        if (score <= options.MediumMaxThreshold) return "Medium";
        if (score <= options.HighMaxThreshold) return "High";
        return "Critical";
    }
}
