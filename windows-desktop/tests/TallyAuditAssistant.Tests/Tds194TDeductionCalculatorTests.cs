using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tds194TDeductionCalculatorTests
{
    private readonly Tds194TDeductionCalculator _calculator = new();

    private static Tds194TDeductionRequest Request(
        DateOnly? date = null,
        bool omitDate = false,
        decimal amount = 25000m,
        decimal prior = 0m,
        bool firm = true,
        bool partner = true,
        bool category = true,
        bool confirmed = true,
        bool pan = true,
        decimal? certificateRate = null,
        string? certificateRef = null,
        DateOnly? validFrom = null,
        DateOnly? validTo = null) =>
        new(omitDate ? null : date ?? new DateOnly(2025, 4, 1), amount, prior,
            firm, partner, category, confirmed, pan, certificateRate,
            certificateRef, validFrom, validTo);

    [Fact]
    public void Calculate_UsesOldAndNewActOnCorrectDates()
    {
        var beforeEffective = _calculator.Calculate(Request(date: new(2025, 3, 31)));
        var oldAct = _calculator.Calculate(Request(date: new(2026, 3, 31)));
        var newAct = _calculator.Calculate(Request(date: new(2026, 4, 1)));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, beforeEffective.Status);
        Assert.Equal("Section 194T", oldAct.SectionReference);
        Assert.Equal("Income-tax Act, 1961", oldAct.GoverningAct);
        Assert.Equal("Section 393(3), Table Sl. No. 7", newAct.SectionReference);
        Assert.Equal("Income-tax Act, 2025", newAct.GoverningAct);
    }

    [Theory]
    [InlineData(20000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(20001, TdsCalculationStatus.Calculated)]
    public void Calculate_AppliesStrictTwentyThousandPartnerYearThreshold(decimal amount, TdsCalculationStatus expected)
    {
        Assert.Equal(expected, _calculator.Calculate(Request(amount: amount)).Status);
    }

    [Fact]
    public void Calculate_DeductsOnFullAggregateNotOnlyExcess()
    {
        var result = _calculator.Calculate(Request(amount: 5000m, prior: 18000m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(23000m, result.TaxableAmount);
        Assert.Equal(2300m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_RequiresFirmPartnerAndApplicabilityConfirmation()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(firm: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(partner: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(confirmed: false)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(category: false)).Status);
    }

    [Fact]
    public void Calculate_RequiresPanAndValidatesLowerDeductionCertificate()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(pan: false)).Status);
        var valid = _calculator.Calculate(Request(certificateRate: 5m, certificateRef: "CERT-194T",
            validFrom: new(2025, 4, 1), validTo: new(2026, 3, 31)));
        Assert.Equal(TdsCalculationStatus.Calculated, valid.Status);
        Assert.Equal(5m, valid.AppliedRatePercent);
        Assert.Equal(1250m, valid.TdsAmount);
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(certificateRate: 12m, certificateRef: "CERT-194T",
                validFrom: new(2025, 4, 1), validTo: new(2026, 3, 31))).Status);
    }

    [Fact]
    public void Calculate_RejectsMissingDateAndNegativeAmounts()
    {
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request(omitDate: true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(amount: -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(prior: -1m)));
    }
}
