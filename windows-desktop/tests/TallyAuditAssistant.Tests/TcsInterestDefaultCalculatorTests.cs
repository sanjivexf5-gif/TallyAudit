using System;
using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class TcsInterestDefaultCalculatorTests
{
    private readonly TcsInterestDefaultCalculator _calculator = new();

    [Theory]
    [InlineData(TcsInterestDefaultType.FailureToCollect, 1.0, 1000.00)]
    [InlineData(TcsInterestDefaultType.FailureToDeposit, 1.5, 1500.00)]
    public void Calculate_AppliesCorrectMonthlyRateAndCalendarMonthCount(
        TcsInterestDefaultType defaultType, decimal rate, decimal expectedInterest)
    {
        var result = _calculator.Calculate(new TcsInterestDefaultRequest(
            defaultType,
            TcsInterestGoverningAct.IncomeTaxAct2025,
            100000m,
            new DateOnly(2026, 4, 30),
            new DateOnly(2026, 5, 1)));

        Assert.Equal(rate, result.MonthlyRatePercent);
        Assert.Equal(2, result.MonthsOrParts);
        Assert.Equal(expectedInterest, result.InterestAmount);
        Assert.Contains("Section 398(3)(a)", result.SectionReference);
    }

    [Fact]
    public void Calculate_SameCalendarMonthCountsAsOneMonthOrPart()
    {
        var result = _calculator.Calculate(new TcsInterestDefaultRequest(
            TcsInterestDefaultType.FailureToCollect,
            TcsInterestGoverningAct.IncomeTaxAct1961,
            50000m,
            new DateOnly(2025, 8, 1),
            new DateOnly(2025, 8, 31)));

        Assert.Equal(1, result.MonthsOrParts);
        Assert.Equal(500m, result.InterestAmount);
        Assert.Contains("Section 206C(7)", result.SectionReference);
    }

    [Fact]
    public void Calculate_CrossYearCountsEveryCalendarMonthTouched()
    {
        var result = _calculator.Calculate(new TcsInterestDefaultRequest(
            TcsInterestDefaultType.FailureToDeposit,
            TcsInterestGoverningAct.IncomeTaxAct2025,
            10000m,
            new DateOnly(2025, 12, 31),
            new DateOnly(2026, 1, 1)));

        Assert.Equal(2, result.MonthsOrParts);
        Assert.Equal(300m, result.InterestAmount);
    }

    [Fact]
    public void Calculate_OldActDepositBeforeApril2025UsesOnePercent()
    {
        var result = _calculator.Calculate(new TcsInterestDefaultRequest(
            TcsInterestDefaultType.FailureToDeposit,
            TcsInterestGoverningAct.IncomeTaxAct1961,
            10000m,
            new DateOnly(2025, 3, 31),
            new DateOnly(2025, 4, 1)));
        Assert.Equal(1.0m, result.MonthlyRatePercent);
        Assert.Equal(100m, result.InterestAmount);
    }

    [Fact]
    public void Calculate_OldActDepositFromApril2025UsesOneAndHalfPercent()
    {
        var result = _calculator.Calculate(new TcsInterestDefaultRequest(
            TcsInterestDefaultType.FailureToDeposit,
            TcsInterestGoverningAct.IncomeTaxAct1961,
            10000m,
            new DateOnly(2025, 4, 1),
            new DateOnly(2025, 4, 30)));
        Assert.Equal(1.5m, result.MonthlyRatePercent);
        Assert.Equal(150m, result.InterestAmount);
    }
    [Fact]
    public void Calculate_RoundsToTwoDecimalsAwayFromZero()
    {
        var result = _calculator.Calculate(new TcsInterestDefaultRequest(
            TcsInterestDefaultType.FailureToCollect,
            TcsInterestGoverningAct.IncomeTaxAct2025,
            10.55m,
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 30)));

        Assert.Equal(0.11m, result.InterestAmount);
    }

    [Fact]
    public void Calculate_RejectsNonPositiveTaxAmount()
    {
        var request = new TcsInterestDefaultRequest(
            TcsInterestDefaultType.FailureToCollect,
            TcsInterestGoverningAct.IncomeTaxAct2025,
            0m,
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 2));

        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(request));
    }

    [Fact]
    public void Calculate_RejectsResolutionBeforeStart()
    {
        var request = new TcsInterestDefaultRequest(
            TcsInterestDefaultType.FailureToDeposit,
            TcsInterestGoverningAct.IncomeTaxAct2025,
            1000m,
            new DateOnly(2026, 4, 2),
            new DateOnly(2026, 4, 1));

        Assert.Throws<ArgumentException>(() => _calculator.Calculate(request));
    }
}
