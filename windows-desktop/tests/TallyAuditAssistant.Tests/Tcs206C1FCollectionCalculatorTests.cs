using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tcs206C1FCollectionCalculatorTests
{
    private readonly Tcs206C1FDeductionCalculator _calculator = new();

    private static Tcs206C1FCollectionRequest Request(
        DateOnly? receipt = null, DateOnly? debit = null,
        decimal itemValue = 1500000m, decimal current = 1500000m,
        bool motorVehicle = true, bool category = true, bool seller = true,
        bool exemptionChecked = true, bool exempt = false, bool pan = true,
        bool lowerCertificate = false, bool omitDates = false) =>
        new(omitDates ? receipt : receipt ?? new DateOnly(2026, 4, 1), debit,
            itemValue, current, motorVehicle, category, seller, exemptionChecked,
            exempt, pan, lowerCertificate);

    [Fact]
    public void Calculate_UsesEarlierDebitOrReceiptForActTransition()
    {
        var result = _calculator.Calculate(Request(
            receipt: new DateOnly(2026, 4, 5),
            debit: new DateOnly(2026, 3, 31)));
        Assert.Equal("Income-tax Act, 1961", result.GoverningAct);
        Assert.Equal("Section 206C(1F)", result.SectionReference);
    }

    [Fact]
    public void Calculate_UsesNewActFromApril2026()
    {
        var result = _calculator.Calculate(Request());
        Assert.Equal("Income-tax Act, 2025", result.GoverningAct);
        Assert.Equal("Section 394(1), Table Sl. No. 6", result.SectionReference);
        Assert.Equal(1m, result.AppliedRatePercent);
        Assert.Equal(15000m, result.TcsAmount);
    }

    [Theory]
    [InlineData(1000000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(1000001, TdsCalculationStatus.Calculated)]
    public void Calculate_UsesStrictPerItemThreshold(decimal itemValue, TdsCalculationStatus expected)
    {
        var result = _calculator.Calculate(Request(itemValue: itemValue, current: itemValue));
        Assert.Equal(expected, result.Status);
        if (expected == TdsCalculationStatus.Calculated)
            Assert.Equal(10000.01m, result.TcsAmount);
    }

    [Fact]
    public void Calculate_CollectsOnEntireCurrentConsiderationNotOnlyExcess()
    {
        var result = _calculator.Calculate(Request(itemValue: 2500000m, current: 2500000m));
        Assert.Equal(2500000m, result.TaxableAmount);
        Assert.Equal(25000m, result.TcsAmount);
    }

    [Fact]
    public void Calculate_AppliesThresholdToItemAndTcsToEachInstallment()
    {
        var result = _calculator.Calculate(Request(itemValue: 2000000m, current: 500000m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(5000m, result.TcsAmount);
    }

    [Fact]
    public void Calculate_NotifiedGoodsAreNotCoveredBeforeNotificationEffectiveDate()
    {
        var result = _calculator.Calculate(Request(
            receipt: new DateOnly(2025, 4, 21), motorVehicle: false));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
    }

    [Fact]
    public void Calculate_NotifiedGoodsAreCoveredFromNotificationDate()
    {
        var result = _calculator.Calculate(Request(
            receipt: new DateOnly(2025, 4, 22), motorVehicle: false));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
    }

    [Fact]
    public void Calculate_RequiresSellerCategoryAndBuyerExemptionReview()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(seller: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(category: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(exemptionChecked: false)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(exempt: true)).Status);
    }

    [Fact]
    public void Calculate_DoesNotAllowLowerRateCertificateOrAssumePanCompliance()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(lowerCertificate: true)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(pan: false)).Status);
    }

    [Fact]
    public void Calculate_RejectsMissingDatesAndNegativeValues()
    {
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request(omitDates: true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(itemValue: -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(current: -1m)));
    }
}
