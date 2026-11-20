using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tds194GDeductionCalculatorTests
{
    private readonly Tds194GDeductionCalculator _calculator = new();

    private static Tds194GDeductionRequest Request(
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
    public void Calculate_UsesEarlierCreditOrPaymentDateForActTransition()
    {
        var result = _calculator.Calculate(Request(
            credit: new DateOnly(2026, 3, 31),
            payment: new DateOnly(2026, 4, 5)));

        Assert.Equal("Income-tax Act, 1961", result.GoverningAct);
        Assert.Equal("Section 194G", result.SectionReference);
    }

    [Fact]
    public void Calculate_UsesNewActTableEntryFromApril2026()
    {
        var result = _calculator.Calculate(Request(payment: new DateOnly(2026, 4, 1)));

        Assert.Equal("Income-tax Act, 2025", result.GoverningAct);
        Assert.Equal("Section 393(3), Table Sl. No. 4", result.SectionReference);
        Assert.Equal(2m, result.AppliedRatePercent);
    }

    [Theory]
    [InlineData(2024, 9, 30, 5, 15000, 15001, 750.05)]
    [InlineData(2024, 10, 1, 2, 15000, 15001, 300.02)]
    [InlineData(2025, 3, 31, 2, 15000, 15001, 300.02)]
    [InlineData(2025, 4, 1, 2, 20000, 20001, 400.02)]
    public void Calculate_AppliesEffectiveDatedRateAndThreshold(
        int year, int month, int day, decimal expectedRate, decimal threshold,
        decimal aggregate, decimal expectedTax)
    {
        var date = new DateOnly(year, month, day);
        var result = _calculator.Calculate(Request(
            credit: date, amount: aggregate, prior: 0m));

        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(expectedRate, result.AppliedRatePercent);
        Assert.Equal(expectedTax, result.TdsAmount);
    }

    [Theory]
    [InlineData(15000, 0, 15000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(10000, 5000, 15000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(15001, 0, 15000, TdsCalculationStatus.Calculated)]
    [InlineData(20000, 0, 20000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(15000, 5000, 20000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(20001, 0, 20000, TdsCalculationStatus.Calculated)]
    public void Calculate_AppliesFinancialYearAggregateThreshold(
        decimal amount, decimal prior, decimal threshold, TdsCalculationStatus expected)
    {
        var date = threshold == 15000m ? new DateOnly(2025, 3, 31) : new DateOnly(2025, 4, 1);
        var result = _calculator.Calculate(Request(credit: date, amount: amount, prior: prior));

        Assert.Equal(expected, result.Status);
        if (expected == TdsCalculationStatus.Calculated)
            Assert.Equal(decimal.Round(amount * 0.02m, 2), result.TdsAmount);
    }

    [Fact]
    public void Calculate_DeductsOnlyOnCurrentEventAfterAggregateThresholdIsCrossed()
    {
        var result = _calculator.Calculate(Request(
            amount: 5000m,
            prior: 18000m));

        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(5000m, result.TaxableAmount);
        Assert.Equal(100m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_UsesValidLowerDeductionCertificate()
    {
        var result = _calculator.Calculate(Request(
            amount: 30000m,
            certificateRate: 1m,
            certificateRef: "CERT-194G-1",
            validFrom: new DateOnly(2026, 4, 1),
            validTo: new DateOnly(2027, 3, 31)));

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
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(prior: -1m)));
    }
}
