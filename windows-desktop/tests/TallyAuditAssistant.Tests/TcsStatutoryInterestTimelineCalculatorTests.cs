using System;
using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class TcsStatutoryInterestTimelineCalculatorTests
{
    private readonly TcsStatutoryInterestTimelineCalculator _calculator = new();

    [Fact]
    public void Calculate_PreApril2025UsesSingleOnePercentStageUntilPayment()
    {
        var result = _calculator.Calculate(new TcsStatutoryInterestRequest(
            TcsInterestRegime.IncomeTaxAct1961BeforeApril2025Amendment,
            10000m, new DateOnly(2025, 2, 28), new DateOnly(2025, 3, 5), new DateOnly(2025, 3, 31)));

        Assert.Single(result.Stages);
        Assert.Equal(1.0m, result.Stages[0].MonthlyRatePercent);
        Assert.Equal(2, result.Stages[0].MonthsOrParts);
        Assert.Equal(200m, result.TotalInterest);
    }

    [Fact]
    public void Calculate_AmendedOldActSeparatesCollectionAndDepositStages()
    {
        var result = _calculator.Calculate(new TcsStatutoryInterestRequest(
            TcsInterestRegime.IncomeTaxAct1961FromApril2025,
            10000m, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30), new DateOnly(2025, 5, 1)));

        Assert.Equal(2, result.Stages.Count);
        Assert.Equal(1.0m, result.Stages[0].MonthlyRatePercent);
        Assert.Equal(1.5m, result.Stages[1].MonthlyRatePercent);
        Assert.Equal(100m, result.Stages[0].InterestAmount);
        Assert.Equal(300m, result.Stages[1].InterestAmount); // Collection-to-payment period touches April and May.
        Assert.Equal(400m, result.TotalInterest);
    }

    [Fact]
    public void Calculate_NewActUsesOneAndOneHalfPercentDepositStage()
    {
        var result = _calculator.Calculate(new TcsStatutoryInterestRequest(
            TcsInterestRegime.IncomeTaxAct2025,
            100000m, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), new DateOnly(2026, 5, 1)));

        Assert.Equal(1000m, result.Stages[0].InterestAmount);
        Assert.Equal(3000m, result.Stages[1].InterestAmount); // Collection-to-payment period touches April and May.
        Assert.Equal(4000m, result.TotalInterest);
        Assert.Contains("Section 398(3)(a)", result.SectionReference);
    }

    [Fact]
    public void Calculate_RequiresCollectionDateForPostAmendment()
    {
        var request = new TcsStatutoryInterestRequest(
            TcsInterestRegime.IncomeTaxAct1961FromApril2025,
            10000m, new DateOnly(2025, 4, 1), null, new DateOnly(2025, 5, 1));

        Assert.Throws<ArgumentException>(() => _calculator.Calculate(request));
    }

    [Fact]
    public void Calculate_RejectsDatesOutsideSelectedRegime()
    {
        var request = new TcsStatutoryInterestRequest(
            TcsInterestRegime.IncomeTaxAct1961FromApril2025,
            10000m, new DateOnly(2025, 3, 31), new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 2));

        Assert.Throws<ArgumentException>(() => _calculator.Calculate(request));
    }

    [Fact]
    public void Calculate_RejectsCollectionAfterPayment()
    {
        var request = new TcsStatutoryInterestRequest(
            TcsInterestRegime.IncomeTaxAct2025,
            10000m, new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 2), new DateOnly(2026, 5, 1));

        Assert.Throws<ArgumentException>(() => _calculator.Calculate(request));
    }
}
