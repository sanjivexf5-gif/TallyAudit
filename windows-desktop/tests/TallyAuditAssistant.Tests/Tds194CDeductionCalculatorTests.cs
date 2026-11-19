using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tds194CDeductionCalculatorTests
{
    private readonly Tds194CDeductionCalculator _calculator = new();

    private static TdsDeductionRequest Request(
        DateOnly? credit = null,
        DateOnly? payment = null,
        decimal amount = 50000m,
        decimal prior = 0m,
        TdsDeducteeEntityType entity = TdsDeducteeEntityType.IndividualOrHuf,
        bool pan = true,
        bool applicability = true,
        decimal? certificateRate = null,
        string? certificateRef = null,
        DateOnly? validFrom = null,
        DateOnly? validTo = null) =>
        new(credit, payment, amount, prior, entity, pan, applicability,
            certificateRate, certificateRef, validFrom, validTo);

    [Fact]
    public void Calculate_UsesEarlierCreditDateForActTransition()
    {
        var result = _calculator.Calculate(Request(new(2026, 3, 31), new(2026, 4, 10)));
        Assert.Equal("Income-tax Act, 1961", result.GoverningAct);
        Assert.Equal("Section 194C", result.SectionReference);
    }

    [Fact]
    public void Calculate_UsesNewActSectionForApril2026Trigger()
    {
        var result = _calculator.Calculate(Request(new(2026, 4, 2), new(2026, 4, 1)));
        Assert.Equal("Income-tax Act, 2025", result.GoverningAct);
        Assert.Equal("Section 393(1), Table Sl. No. 6(i)", result.SectionReference);
    }

    [Fact]
    public void Calculate_DoesNotDeductWhenBothThresholdsAreNotExceeded()
    {
        var result = _calculator.Calculate(Request(amount: 30000m, prior: 70000m));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
        Assert.Equal(0m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_AppliesAggregateThresholdWhenCurrentPaymentIsSmall()
    {
        var result = _calculator.Calculate(Request(amount: 20000m, prior: 90000m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(1m, result.AppliedRatePercent);
        Assert.Equal(200m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_UsesOnePercentForIndividualAndTwoPercentForOtherEntities()
    {
        var individual = _calculator.Calculate(Request(amount: 50000m));
        var company = _calculator.Calculate(Request(amount: 50000m, entity: TdsDeducteeEntityType.Other));
        Assert.Equal(500m, individual.TdsAmount);
        Assert.Equal(1000m, company.TdsAmount);
    }

    [Fact]
    public void Calculate_AppliesValidLowerDeductionCertificate()
    {
        var result = _calculator.Calculate(Request(amount: 50000m, certificateRate: 0.5m,
            certificateRef: "CERT-1", validFrom: new(2026, 4, 1), validTo: new(2027, 3, 31)));
        Assert.Equal(0.5m, result.AppliedRatePercent);
        Assert.Equal(250m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_RequiresReviewForMissingPanOrUnconfirmedApplicability()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(pan: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(applicability: false)).Status);
    }

    [Fact]
    public void Calculate_RequiresCompleteCurrentCertificate()
    {
        var result = _calculator.Calculate(Request(certificateRate: 0m, certificateRef: "CERT-1"));
        Assert.Equal(TdsCalculationStatus.ReviewRequired, result.Status);
    }

    [Fact]
    public void Calculate_RejectsMissingDatesAndNegativeAmounts()
    {
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request()));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(new(2026, 4, 1), amount: -1m)));
    }
}
