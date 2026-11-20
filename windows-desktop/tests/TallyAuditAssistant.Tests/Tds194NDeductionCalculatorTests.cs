using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tds194NDeductionCalculatorTests
{
    private readonly Tds194NDeductionCalculator _calculator = new();

    private static Tds194NDeductionRequest Request(
        DateOnly? date = null,
        decimal amount = 11000000m,
        decimal prior = 0m,
        bool payer = true,
        bool cooperativeRecipient = false,
        bool exempt = false,
        bool aggregationConfirmed = true,
        bool applicabilityConfirmed = true,
        bool pan = true,
        bool filingStatusConfirmed = true,
        bool returnsFiled = true) =>
        new(date ?? new DateOnly(2025, 4, 1), amount, prior, payer,
            cooperativeRecipient, exempt, aggregationConfirmed,
            applicabilityConfirmed, pan, filingStatusConfirmed, returnsFiled);

    [Fact]
    public void Calculate_UsesEffectiveDateAndNewActTransition()
    {
        var before = _calculator.Calculate(Request(date: new(2019, 8, 31)));
        var oldAct = _calculator.Calculate(Request(date: new(2026, 3, 31)));
        var newAct = _calculator.Calculate(Request(date: new(2026, 4, 1)));

        Assert.Equal(TdsCalculationStatus.BelowThreshold, before.Status);
        Assert.Equal("Section 194N", oldAct.SectionReference);
        Assert.Equal("Income-tax Act, 1961", oldAct.GoverningAct);
        Assert.Equal("Section 393(3), Table Sl. No. 5", newAct.SectionReference);
        Assert.Equal("Income-tax Act, 2025", newAct.GoverningAct);
        Assert.Equal(2m, newAct.AppliedRatePercent);
        Assert.Contains("does not carry forward the old section 194N non-filer", newAct.Explanation);
    }

    [Theory]
    [InlineData(10000000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(10000001, TdsCalculationStatus.Calculated)]
    public void Calculate_AppliesOneCroreThresholdForOrdinaryRecipients(decimal aggregate, TdsCalculationStatus expected)
    {
        Assert.Equal(expected, _calculator.Calculate(Request(date: new(2026, 4, 1), amount: aggregate)).Status);
    }

    [Fact]
    public void Calculate_UsesThreeCroreThresholdForCooperativeSocietyRecipients()
    {
        var below = _calculator.Calculate(Request(date: new(2026, 4, 1),
            amount: 25000000m, cooperativeRecipient: true));
        var above = _calculator.Calculate(Request(date: new(2026, 4, 1),
            amount: 30000001m, cooperativeRecipient: true));

        Assert.Equal(TdsCalculationStatus.BelowThreshold, below.Status);
        Assert.Equal(TdsCalculationStatus.Calculated, above.Status);
        Assert.Equal(600000.02m, above.TdsAmount);
    }

    [Fact]
    public void Calculate_OldActNonFilerUsesTwentyLakhAndFivePercentTiers()
    {
        var below = _calculator.Calculate(Request(date: new(2025, 4, 1),
            amount: 2000000m, returnsFiled: false));
        var twoPercent = _calculator.Calculate(Request(date: new(2025, 4, 1),
            amount: 5000000m, returnsFiled: false));
        var fivePercent = _calculator.Calculate(Request(date: new(2025, 4, 1),
            amount: 11000000m, returnsFiled: false));

        Assert.Equal(TdsCalculationStatus.BelowThreshold, below.Status);
        Assert.Equal(2m, twoPercent.AppliedRatePercent);
        Assert.Equal(5m, fivePercent.AppliedRatePercent);
        Assert.Equal(550000m, fivePercent.TdsAmount);
    }

    [Fact]
    public void Calculate_OldActCooperativeNonFilerUsesThreeCroreThreshold()
    {
        var result = _calculator.Calculate(Request(date: new(2025, 4, 1),
            amount: 25000000m, cooperativeRecipient: true, returnsFiled: false));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
    }

    [Fact]
    public void Calculate_TaxesOnlyCurrentWithdrawalWhenPriorAggregateCrossesThreshold()
    {
        var result = _calculator.Calculate(Request(date: new(2026, 4, 1),
            amount: 200000m, prior: 9900000m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(200000m, result.TaxableAmount);
        Assert.Equal(4000m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_ReturnsReviewForUnconfirmedNonFilerStatusAndMissingPan()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(date: new(2025, 4, 1), filingStatusConfirmed: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(date: new(2026, 4, 1), pan: false)).Status);
    }

    [Fact]
    public void Calculate_ValidatesPayerExemptionsAndAggregation()
    {
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(payer: false)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(exempt: true)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(aggregationConfirmed: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(applicabilityConfirmed: false)).Status);
    }

    [Fact]
    public void Calculate_RejectsMissingDateAndNegativeAmounts()
    {
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request(date: null) with { WithdrawalDate = null }));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(amount: -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(prior: -1m)));
    }
}
