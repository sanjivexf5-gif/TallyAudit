using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tds194IADeductionCalculatorTests
{
    private readonly Tds194IADeductionCalculator _calculator = new();

    private static Tds194IADeductionRequest Request(
        DateOnly? credit = null,
        DateOnly? payment = null,
        decimal consideration = 6000000m,
        decimal stampValue = 6000000m,
        decimal current = 6000000m,
        bool resident = true,
        bool agricultural = false,
        bool compulsory = false,
        bool confirmed = true,
        bool pan = true,
        bool aggregationConfirmed = true,
        decimal? aggregate = null,
        decimal? certificateRate = null,
        string? certificateRef = null,
        DateOnly? validFrom = null,
        DateOnly? validTo = null,
        bool? lastOrLumpSum = false,
        decimal? proportionateStampValue = null,
        decimal? previousInstallments = null,
        decimal? totalPaidIncludingCurrent = null) =>
        new(credit ?? new DateOnly(2024, 10, 1), payment, consideration,
            stampValue, current, resident, agricultural, compulsory,
            confirmed, pan, aggregationConfirmed, aggregate,
            certificateRate, certificateRef, validFrom, validTo,
            lastOrLumpSum, proportionateStampValue, previousInstallments,
            totalPaidIncludingCurrent);

    [Fact]
    public void Calculate_UsesEffectiveDateAndNewActTransition()
    {
        var before = _calculator.Calculate(Request(credit: new(2013, 5, 31)));
        var oldAct = _calculator.Calculate(Request(credit: new(2026, 3, 31)));
        var newAct = _calculator.Calculate(Request(credit: new(2026, 4, 1)));

        Assert.Equal(TdsCalculationStatus.BelowThreshold, before.Status);
        Assert.Equal("Section 194-IA", oldAct.SectionReference);
        Assert.Equal("Income-tax Act, 1961", oldAct.GoverningAct);
        Assert.Equal("Section 393(1), Table Sl. No. 3(i)", newAct.SectionReference);
        Assert.Equal("Income-tax Act, 2025", newAct.GoverningAct);
        Assert.Contains("Form 141 Schedule B", newAct.Explanation);
    }

    [Fact]
    public void Calculate_OldActRequiresBothConsiderationAndStampValueBelowThresholdToAvoidDeduction()
    {
        var bothBelow = _calculator.Calculate(Request(consideration: 4900000m, stampValue: 4900000m, current: 4900000m));
        var considerationBelowStampAbove = _calculator.Calculate(Request(consideration: 4900000m, stampValue: 5100000m, current: 4900000m));
        var considerationAboveStampBelow = _calculator.Calculate(Request(consideration: 5100000m, stampValue: 4900000m, current: 5100000m));

        Assert.Equal(TdsCalculationStatus.BelowThreshold, bothBelow.Status);
        Assert.Equal(TdsCalculationStatus.Calculated, considerationBelowStampAbove.Status);
        Assert.Equal(49000m, considerationBelowStampAbove.TdsAmount);
        Assert.Equal(TdsCalculationStatus.Calculated, considerationAboveStampBelow.Status);
    }

    [Fact]
    public void Calculate_NewActUsesHigherOfConsiderationOrStampValue()
    {
        var result = _calculator.Calculate(Request(credit: new(2026, 4, 1),
            consideration: 6000000m, stampValue: 7000000m, current: 6000000m,
            lastOrLumpSum: true, proportionateStampValue: 7000000m,
            previousInstallments: 0m, totalPaidIncludingCurrent: 6000000m));
        Assert.Equal(1m, result.AppliedRatePercent);
        Assert.Equal(7000000m, result.TaxableAmount);
        Assert.Equal(70000m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_UsesPropertyLevelThresholdAndCurrentInstallmentOnly()
    {
        var result = _calculator.Calculate(Request(credit: new(2026, 4, 1),
            consideration: 8000000m, stampValue: 8000000m, current: 1000000m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(1000000m, result.TaxableAmount);
        Assert.Equal(10000m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_NewActLastInstallmentUsesProportionateStampValueAndCumulativePaidAmount()
    {
        var result = _calculator.Calculate(Request(credit: new(2026, 4, 1),
            consideration: 8000000m, stampValue: 9000000m, current: 1000000m,
            lastOrLumpSum: true, proportionateStampValue: 4500000m,
            previousInstallments: 3000000m, totalPaidIncludingCurrent: 4000000m));

        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(4500000m, result.TaxableAmount);
        Assert.Equal(45000m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_NewActRequiresForm141InstallmentDetailsForLastOrLumpSum()
    {
        var result = _calculator.Calculate(Request(credit: new(2026, 4, 1),
            lastOrLumpSum: true));
        Assert.Equal(TdsCalculationStatus.ReviewRequired, result.Status);
    }

    [Fact]
    public void Calculate_AggregatesJointBuyersAndSellersFromOctober2024()
    {
        var result = _calculator.Calculate(Request(credit: new(2024, 10, 1),
            consideration: 3000000m, stampValue: 3000000m, current: 1500000m,
            aggregate: 6000000m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(15000m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_RequiresPropertyAggregationConfirmationUnderNewAct()
    {
        var result = _calculator.Calculate(Request(credit: new(2026, 4, 1),
            aggregationConfirmed: false));
        Assert.Equal(TdsCalculationStatus.ReviewRequired, result.Status);
    }

    [Fact]
    public void Calculate_ReviewsNonResidentSellerAndMissingPan()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(resident: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(pan: false)).Status);
    }

    [Fact]
    public void Calculate_ExcludesAgriculturalLandAndCompulsoryAcquisition()
    {
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(agricultural: true)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(compulsory: true)).Status);
    }

    [Fact]
    public void Calculate_ValidatesLowerDeductionCertificate()
    {
        var result = _calculator.Calculate(Request(certificateRate: 0.5m,
            certificateRef: "CERT-194IA", validFrom: new(2024, 10, 1), validTo: new(2025, 3, 31)));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(0.5m, result.AppliedRatePercent);
        Assert.Equal(30000m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_RejectsMissingDatesAndNegativeAmounts()
    {
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request(credit: null, payment: null) with { CreditDate = null }));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(consideration: -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(current: -1m)));
    }
}
