using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tds194KDeductionCalculatorTests
{
    private readonly Tds194KDeductionCalculator _calculator = new();

    private static Tds194KDeductionRequest Request(
        DateOnly? credit = null, DateOnly? payment = null,
        decimal amount = 15000m, decimal prior = 0m,
        bool resident = true, bool capitalGain = false,
        bool pan = true, bool applicability = true,
        decimal? certificateRate = null, string? certificateRef = null,
        DateOnly? validFrom = null, DateOnly? validTo = null,
        bool omitDates = false) =>
        new(omitDates ? credit : credit ?? new DateOnly(2026, 4, 1), payment,
            amount, prior, resident, capitalGain, pan, applicability,
            certificateRate, certificateRef, validFrom, validTo);

    [Fact]
    public void Calculate_UsesEarlierCreditOrPaymentDateForActTransition()
    {
        var result = _calculator.Calculate(Request(
            credit: new DateOnly(2026, 3, 31), payment: new DateOnly(2026, 4, 5)));
        Assert.Equal("Income-tax Act, 1961", result.GoverningAct);
        Assert.Equal("Section 194K", result.SectionReference);
    }

    [Fact]
    public void Calculate_UsesNewActTableEntryFromApril2026()
    {
        var result = _calculator.Calculate(Request());
        Assert.Equal("Income-tax Act, 2025", result.GoverningAct);
        Assert.Equal("Section 393(1), Table Sl. No. 4(i)", result.SectionReference);
        Assert.Equal(10m, result.AppliedRatePercent);
        Assert.Equal(1500m, result.TdsAmount);
    }

    [Theory]
    [InlineData(2025, 3, 31, 5000, 5000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(2025, 3, 31, 5001, 5000, TdsCalculationStatus.Calculated)]
    [InlineData(2025, 4, 1, 10000, 10000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(2025, 4, 1, 10001, 10000, TdsCalculationStatus.Calculated)]
    public void Calculate_AppliesEffectiveDatedAnnualThreshold(
        int year, int month, int day, decimal amount, decimal threshold, TdsCalculationStatus expected)
    {
        var result = _calculator.Calculate(Request(
            credit: new DateOnly(year, month, day), amount: amount));
        Assert.Equal(expected, result.Status);
        if (expected == TdsCalculationStatus.Calculated)
        {
            Assert.Equal(10m, result.AppliedRatePercent);
            Assert.Equal(amount * 0.10m, result.TdsAmount);
        }
    }

    [Theory]
    [InlineData(4000, 1000, 5000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(4000, 1001, 5000, TdsCalculationStatus.Calculated)]
    [InlineData(9000, 1000, 10000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(9000, 1001, 10000, TdsCalculationStatus.Calculated)]
    public void Calculate_UsesPriorFinancialYearAggregateForThreshold(
        decimal current, decimal prior, decimal threshold, TdsCalculationStatus expected)
    {
        var date = threshold == 5000m ? new DateOnly(2025, 3, 31) : new DateOnly(2025, 4, 1);
        var result = _calculator.Calculate(Request(credit: date, amount: current, prior: prior));
        Assert.Equal(expected, result.Status);
        if (expected == TdsCalculationStatus.Calculated)
            Assert.Equal(current * 0.10m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_DeductsOnCurrentEventWhenAggregateCrossesThreshold()
    {
        var result = _calculator.Calculate(Request(amount: 2000m, prior: 9000m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(2000m, result.TaxableAmount);
        Assert.Equal(200m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_ExcludesCapitalGains()
    {
        var result = _calculator.Calculate(Request(capitalGain: true));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
        Assert.Equal(0m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_RequiresResidentRecipientAndConfirmedEligibility()
    {
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(resident: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(applicability: false)).Status);
    }

    [Fact]
    public void Calculate_RequiresPanOrCertificateEvidence()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(pan: false)).Status);
        var certified = _calculator.Calculate(Request(amount: 20000m, certificateRate: 5m,
            certificateRef: "CERT-194K-1", validFrom: new DateOnly(2026, 4, 1),
            validTo: new DateOnly(2027, 3, 31)));
        Assert.Equal(5m, certified.AppliedRatePercent);
        Assert.Equal(1000m, certified.TdsAmount);
    }

    [Fact]
    public void Calculate_RejectsMissingDatesAndNegativeAmounts()
    {
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request(omitDates: true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(amount: -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(prior: -1m)));
    }
}
