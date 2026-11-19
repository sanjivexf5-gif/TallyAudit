using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tds194JDeductionCalculatorTests
{
    private readonly Tds194JDeductionCalculator _calculator = new();

    private static Tds194JDeductionRequest Request(
        Tds194JPaymentCategory category = Tds194JPaymentCategory.ProfessionalServices,
        DateOnly? credit = null,
        DateOnly? payment = null,
        decimal amount = 60000m,
        decimal prior = 0m,
        bool pan = true,
        bool applicability = true,
        decimal? certificateRate = null,
        string? certificateRef = null,
        DateOnly? validFrom = null,
        DateOnly? validTo = null,
        bool omitDates = false) =>
        new(omitDates ? credit : credit ?? new DateOnly(2026, 4, 1), payment, amount, prior,
            category, pan, applicability, certificateRate, certificateRef, validFrom, validTo);

    [Fact]
    public void Calculate_UsesEarlierEventForActTransition()
    {
        var result = _calculator.Calculate(Request(credit: new(2026, 3, 31), payment: new(2026, 4, 5)));
        Assert.Equal("Income-tax Act, 1961", result.GoverningAct);
        Assert.Equal("Section 194J", result.SectionReference);
    }

    [Fact]
    public void Calculate_UsesNewActTableEntryFromApril2026()
    {
        var result = _calculator.Calculate(Request(payment: new(2026, 4, 1)));
        Assert.Equal("Income-tax Act, 2025", result.GoverningAct);
        Assert.Equal("Section 393(1), Table Sl. No. 6(iii)", result.SectionReference);
    }

    [Theory]
    [InlineData(Tds194JPaymentCategory.ProfessionalServices, 10)]
    [InlineData(Tds194JPaymentCategory.TechnicalServices, 2)]
    [InlineData(Tds194JPaymentCategory.CinematographicFilmRoyalty, 2)]
    [InlineData(Tds194JPaymentCategory.OtherRoyalty, 10)]
    [InlineData(Tds194JPaymentCategory.CallCentreServices, 2)]
    [InlineData(Tds194JPaymentCategory.Section28VaNonCompeteFee, 10)]
    public void Calculate_AppliesCategoryRate(Tds194JPaymentCategory category, decimal expectedRate)
    {
        var result = _calculator.Calculate(Request(category: category, amount: 60000m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(expectedRate, result.AppliedRatePercent);
        Assert.Equal(60000m * expectedRate / 100m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_DoesNotDeductAtOrBelowAnnualThreshold()
    {
        var atThreshold = _calculator.Calculate(Request(amount: 30000m, prior: 20000m));
        var belowThreshold = _calculator.Calculate(Request(amount: 20000m, prior: 20000m));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, atThreshold.Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, belowThreshold.Status);
    }

    [Fact]
    public void Calculate_DeductsWhenAggregateExceedsThreshold()
    {
        var result = _calculator.Calculate(Request(amount: 10000m, prior: 45000m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(1000m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_DirectorRemunerationHasNoThreshold()
    {
        var result = _calculator.Calculate(Request(category: Tds194JPaymentCategory.DirectorRemuneration, amount: 100m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(10m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_UsesValidLowerDeductionCertificate()
    {
        var result = _calculator.Calculate(Request(amount: 60000m, certificateRate: 5m,
            certificateRef: "CERT-1", validFrom: new(2026, 4, 1), validTo: new(2027, 3, 31)));
        Assert.Equal(5m, result.AppliedRatePercent);
        Assert.Equal(3000m, result.TdsAmount);
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
