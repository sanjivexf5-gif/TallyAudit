using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tcs206C1GRemittanceCalculatorTests
{
    private readonly Tcs206C1GRemittanceCalculator _calculator = new();

    private static Tcs206C1GCollectionRequest Request(
        Tcs206C1GTransactionType type = Tcs206C1GTransactionType.LrsRemittance,
        DateOnly? receipt = null, DateOnly? debit = null,
        decimal prior = 0m, decimal current = 1500000m,
        Tcs206C1GLrsPurpose purpose = Tcs206C1GLrsPurpose.Other,
        bool collector = true, bool scope = true, bool exemptionChecked = true,
        bool exempt = false, bool pan = true, bool purposeConfirmed = true,
        bool educationLoan = false, bool packageTcs = false, bool tdsDeducted = false,
        bool omitDates = false) =>
        new(type, omitDates ? receipt : receipt ?? new DateOnly(2026, 4, 1), debit,
            prior, current, collector, scope, exemptionChecked, exempt, pan,
            purpose, purposeConfirmed, educationLoan, packageTcs, tdsDeducted);

    [Fact]
    public void Lrs_EducationMedicalUsesTwoPercentUnderNewAct()
    {
        var result = _calculator.Calculate(Request(purpose: Tcs206C1GLrsPurpose.EducationOrMedicalTreatment,
            prior: 900000m, current: 300000m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(2m, result.AppliedRatePercent);
        Assert.Equal(200000m, result.TaxableAmount);
        Assert.Equal(4000m, result.TcsAmount);
        Assert.Equal("Section 394(1), Table Sl. No. 7", result.SectionReference);
    }

    [Fact]
    public void Lrs_OtherPurposeRemainsTwentyPercentUnderNewAct()
    {
        var result = _calculator.Calculate(Request(prior: 1000000m, current: 500000m));
        Assert.Equal(20m, result.AppliedRatePercent);
        Assert.Equal(100000m, result.TcsAmount);
    }

    [Fact]
    public void Lrs_UsesOneSharedThresholdAcrossRemittances()
    {
        var result = _calculator.Calculate(Request(prior: 800000m, current: 500000m,
            purpose: Tcs206C1GLrsPurpose.Other));
        Assert.Equal(300000m, result.TaxableAmount);
        Assert.Equal(60000m, result.TcsAmount);
    }

    [Fact]
    public void Lrs_DoesNotTaxWhenAggregateDoesNotExceedThreshold()
    {
        var result = _calculator.Calculate(Request(prior: 900000m, current: 100000m));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
        Assert.Equal(0m, result.TcsAmount);
    }

    [Fact]
    public void Lrs_EducationLoanFromQualifyingFinancialInstitutionIsExcluded()
    {
        var result = _calculator.Calculate(Request(
            purpose: Tcs206C1GLrsPurpose.EducationOrMedicalTreatment, educationLoan: true));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
    }

    [Fact]
    public void Lrs_DoesNotDoubleCollectOnTourPackageAmount()
    {
        var result = _calculator.Calculate(Request(packageTcs: true));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
    }

    [Fact]
    public void Lrs_UsesOldActRateBeforeApril2026()
    {
        var result = _calculator.Calculate(Request(receipt: new DateOnly(2026, 3, 31),
            prior: 1000000m, current: 100000m,
            purpose: Tcs206C1GLrsPurpose.EducationOrMedicalTreatment));
        Assert.Equal("Income-tax Act, 1961", result.GoverningAct);
        Assert.Equal(5m, result.AppliedRatePercent);
        Assert.Equal(5000m, result.TcsAmount);
    }

    [Fact]
    public void TourPackage_NewActChargesTwoPercentWithoutThreshold()
    {
        var result = _calculator.Calculate(Request(type: Tcs206C1GTransactionType.OverseasTourPackage,
            current: 50000m));
        Assert.Equal(2m, result.AppliedRatePercent);
        Assert.Equal(1000m, result.TcsAmount);
        Assert.Equal("Section 394(1), Table Sl. No. 8", result.SectionReference);
    }

    [Fact]
    public void TourPackage_OldActSplitsAggregateAtTenLakh()
    {
        var result = _calculator.Calculate(Request(type: Tcs206C1GTransactionType.OverseasTourPackage,
            receipt: new DateOnly(2026, 3, 31), prior: 900000m, current: 300000m));
        Assert.Equal("Income-tax Act, 1961", result.GoverningAct);
        Assert.Equal(5m * 100000m / 100m + 20m * 200000m / 100m, result.TcsAmount);
    }

    [Fact]
    public void TourPackage_DoesNotDoubleCollect()
    {
        var result = _calculator.Calculate(Request(type: Tcs206C1GTransactionType.OverseasTourPackage,
            packageTcs: true));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
    }

    [Fact]
    public void UsesEarlierDebitOrReceiptForActTransition()
    {
        var result = _calculator.Calculate(Request(receipt: new DateOnly(2026, 4, 5),
            debit: new DateOnly(2026, 3, 31), prior: 1000000m, current: 100000m));
        Assert.Equal("Income-tax Act, 1961", result.GoverningAct);
    }

    [Fact]
    public void RequiresCollectorScopeAndExemptionReview()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(collector: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(scope: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(exemptionChecked: false)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(exempt: true)).Status);
    }

    [Fact]
    public void BuyerTdsDeductionExceptionAvoidsTcs()
    {
        var result = _calculator.Calculate(Request(tdsDeducted: true));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
    }

    [Fact]
    public void MissingPanRequiresReviewRatherThanAssumingHigherRate()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(pan: false)).Status);
    }

    [Fact]
    public void RejectsMissingDatesAndNegativeAmounts()
    {
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request(omitDates: true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(current: -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(prior: -1m)));
    }
}
