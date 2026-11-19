using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tds194HDeductionCalculatorTests
{
    private readonly Tds194HDeductionCalculator _calculator = new();

    private static Tds194HDeductionRequest Request(
        DateOnly? credit = null,
        DateOnly? payment = null,
        decimal amount = 25000m,
        decimal prior = 0m,
        bool pan = true,
        bool applicability = true,
        decimal? certificateRate = null,
        string? certificateRef = null,
        DateOnly? validFrom = null,
        DateOnly? validTo = null,
        bool omitDates = false) =>
        new(omitDates ? credit : credit ?? new DateOnly(2026, 4, 1), payment, amount, prior,
            pan, applicability, certificateRate, certificateRef, validFrom, validTo);

    [Fact]
    public void Calculate_UsesEarlierEventForActTransition()
    {
        var result = _calculator.Calculate(Request(credit: new(2026, 3, 31), payment: new(2026, 4, 5)));
        Assert.Equal("Income-tax Act, 1961", result.GoverningAct);
        Assert.Equal("Section 194H", result.SectionReference);
    }

    [Fact]
    public void Calculate_UsesNewActTableEntryFromApril2026()
    {
        var result = _calculator.Calculate(Request(payment: new(2026, 4, 1)));
        Assert.Equal("Income-tax Act, 2025", result.GoverningAct);
        Assert.Equal("Section 393(1), Table Sl. No. 1(ii)", result.SectionReference);
    }

    [Theory]
    [InlineData(20000, 0, TdsCalculationStatus.BelowThreshold)]
    [InlineData(15000, 5000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(20001, 0, TdsCalculationStatus.Calculated)]
    [InlineData(1000, 20000, TdsCalculationStatus.Calculated)]
    public void Calculate_AppliesAnnualThreshold(decimal amount, decimal prior, TdsCalculationStatus expected)
    {
        var result = _calculator.Calculate(Request(amount: amount, prior: prior));
        Assert.Equal(expected, result.Status);
        if (expected == TdsCalculationStatus.Calculated)
        {
            Assert.Equal(2m, result.AppliedRatePercent);
            Assert.Equal(decimal.Round(amount * 0.02m, 2), result.TdsAmount);
        }
    }

    [Fact]
    public void Calculate_UsesValidLowerDeductionCertificate()
    {
        var result = _calculator.Calculate(Request(amount: 30000m, certificateRate: 1m,
            certificateRef: "CERT-1", validFrom: new(2026, 4, 1), validTo: new(2027, 3, 31)));
        Assert.Equal(1m, result.AppliedRatePercent);
        Assert.Equal(300m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_RequiresReviewForMissingPanOrUnconfirmedApplicability()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(pan: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(applicability: false)).Status);
    }

    [Fact]
    public void Calculate_RejectsMissingDatesAndNegativeAmounts()
    {
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request(omitDates: true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(amount: -1m)));
    }
}
