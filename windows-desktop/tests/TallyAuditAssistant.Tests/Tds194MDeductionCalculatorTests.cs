using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tds194MDeductionCalculatorTests
{
    private readonly Tds194MDeductionCalculator _calculator = new();

    private static Tds194MDeductionRequest Request(
        DateOnly? credit = null, DateOnly? payment = null,
        decimal amount = 5000001m, decimal prior = 0m,
        bool individualHuf = true, bool coveredByOtherSection = false,
        bool resident = true, bool category = true, bool confirmed = true,
        bool pan = true, decimal? certificateRate = null,
        string? certificateRef = null, DateOnly? validFrom = null, DateOnly? validTo = null) =>
        new(credit ?? new DateOnly(2024, 10, 1), payment, amount, prior,
            individualHuf, coveredByOtherSection, resident, category, confirmed,
            pan, certificateRate, certificateRef, validFrom, validTo);

    [Fact]
    public void Calculate_UsesCorrectEffectiveDateRateAndActTransition()
    {
        var beforeEffective = _calculator.Calculate(Request(credit: new(2019, 8, 31)));
        var oldRate = _calculator.Calculate(Request(credit: new(2024, 9, 30)));
        var changedRate = _calculator.Calculate(Request(credit: new(2024, 10, 1)));
        var newAct = _calculator.Calculate(Request(credit: new(2026, 4, 1)));

        Assert.Equal(TdsCalculationStatus.BelowThreshold, beforeEffective.Status);
        Assert.Equal(5m, oldRate.AppliedRatePercent);
        Assert.Equal("Section 194M", oldRate.SectionReference);
        Assert.Equal(2m, changedRate.AppliedRatePercent);
        Assert.Equal("Section 393(1), Table Sl. No. 6(ii)", newAct.SectionReference);
        Assert.Equal("Income-tax Act, 2025", newAct.GoverningAct);
        Assert.Contains("Form 141 Schedule C", newAct.Explanation);
    }

    [Theory]
    [InlineData(5000000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(5000001, TdsCalculationStatus.Calculated)]
    public void Calculate_AppliesStrictFiftyLakhAnnualThreshold(decimal amount, TdsCalculationStatus expected)
    {
        Assert.Equal(expected, _calculator.Calculate(Request(amount: amount)).Status);
    }

    [Fact]
    public void Calculate_TaxesOnlyCurrentPaymentAfterAggregateCrossesThreshold()
    {
        var result = _calculator.Calculate(Request(amount: 100000m, prior: 4950000m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(100000m, result.TaxableAmount);
        Assert.Equal(2000m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_UsesEarlierOfCreditAndPayment()
    {
        var result = _calculator.Calculate(Request(
            credit: new(2024, 9, 30), payment: new(2024, 10, 2)));
        Assert.Equal(5m, result.AppliedRatePercent);
        Assert.Equal("Section 194M", result.SectionReference);
    }

    [Fact]
    public void Calculate_RequiresEligiblePayerResidentPayeeAndCoveredCategory()
    {
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(individualHuf: false)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(coveredByOtherSection: true)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(resident: false)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(category: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(confirmed: false)).Status);
    }

    [Fact]
    public void Calculate_RequiresPanAndValidatesCertificate()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(pan: false)).Status);
        var valid = _calculator.Calculate(Request(amount: 100000m, prior: 4950000m,
            certificateRate: 1m, certificateRef: "CERT-194M",
            validFrom: new(2024, 10, 1), validTo: new(2025, 3, 31)));
        Assert.Equal(TdsCalculationStatus.Calculated, valid.Status);
        Assert.Equal(1m, valid.AppliedRatePercent);
        Assert.Equal(100000m, valid.TaxableAmount);
        Assert.Equal(1000m, valid.TdsAmount);
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(certificateRate: 6m, certificateRef: "CERT-194M",
                validFrom: new(2024, 10, 1), validTo: new(2025, 3, 31))).Status);
    }

    [Fact]
    public void Calculate_RejectsMissingDatesAndNegativeAmounts()
    {
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request(credit: null, payment: null)
            with { CreditDate = null }));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(amount: -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(prior: -1m)));
    }
}
