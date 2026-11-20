using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tcs206C1SpecifiedGoodsCalculatorTests
{
    private readonly Tcs206C1SpecifiedGoodsCalculator _calculator = new();

    private static Tcs206C1SpecifiedGoodsRequest Request(
        Tcs206C1GoodsCategory category = Tcs206C1GoodsCategory.Scrap,
        DateOnly? receipt = null, DateOnly? debit = null,
        decimal current = 100000m, bool seller = true, bool buyerChecked = true,
        bool buyerExcluded = false, bool pan = true, bool forestLease = true,
        bool declaration = false, bool declarationValid = false, bool resident = false,
        bool qualifyingUse = false, bool trading = true, bool certificate = false,
        bool certificateValid = false, decimal? certificateRate = null, bool omitDates = false) =>
        new(category, omitDates ? receipt : receipt ?? new DateOnly(2026, 4, 1), debit,
            current, seller, buyerChecked, buyerExcluded, pan, forestLease,
            declaration, declarationValid, resident, qualifyingUse, trading,
            certificate, certificateValid, certificateRate);

    [Theory]
    [InlineData(Tcs206C1GoodsCategory.AlcoholicLiquorForHumanConsumption, 1, 2)]
    [InlineData(Tcs206C1GoodsCategory.TenduLeaves, 5, 2)]
    [InlineData(Tcs206C1GoodsCategory.TimberUnderForestLease, 2, 2)]
    [InlineData(Tcs206C1GoodsCategory.TimberOtherThanForestLease, 2, 2)]
    [InlineData(Tcs206C1GoodsCategory.OtherForestProduceUnderForestLease, 2, 2)]
    [InlineData(Tcs206C1GoodsCategory.Scrap, 1, 2)]
    [InlineData(Tcs206C1GoodsCategory.Coal, 1, 2)]
    [InlineData(Tcs206C1GoodsCategory.Lignite, 1, 2)]
    [InlineData(Tcs206C1GoodsCategory.IronOre, 1, 2)]
    public void Calculate_UsesCorrectCategoryRatesAcrossActTransition(
        Tcs206C1GoodsCategory category, decimal oldRate, decimal newRate)
    {
        var oldResult = _calculator.Calculate(Request(category,
            receipt: new DateOnly(2026, 3, 31)));
        var newResult = _calculator.Calculate(Request(category,
            receipt: new DateOnly(2026, 4, 1)));
        Assert.Equal(oldRate, oldResult.AppliedRatePercent);
        Assert.Equal(newRate, newResult.AppliedRatePercent);
        Assert.Equal(oldRate * 1000m, oldResult.TcsAmount);
        Assert.Equal(newRate * 1000m, newResult.TcsAmount);
    }

    [Fact]
    public void Calculate_UsesEarlierDebitOrReceiptForActTransition()
    {
        var result = _calculator.Calculate(Request(
            receipt: new DateOnly(2026, 4, 5), debit: new DateOnly(2026, 3, 31)));
        Assert.Equal("Income-tax Act, 1961", result.GoverningAct);
        Assert.Equal("Section 206C(1)", result.SectionReference);
    }

    [Fact]
    public void Calculate_UsesNewActTableNumber()
    {
        var result = _calculator.Calculate(Request(Tcs206C1GoodsCategory.IronOre));
        Assert.Equal("Income-tax Act, 2025", result.GoverningAct);
        Assert.Equal("Section 394(1), Table Sl. No. 5", result.SectionReference);
    }

    [Fact]
    public void Calculate_RequiresSellerAndBuyerEligibilityReview()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(seller: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(buyerChecked: false)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(buyerExcluded: true)).Status);
    }

    [Fact]
    public void Calculate_RequiresPanBeforeApplyingNormalRate()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(pan: false)).Status);
    }

    [Fact]
    public void Calculate_Form127Or27CExemptsQualifyingResidentBuyerUse()
    {
        var result = _calculator.Calculate(Request(
            declaration: true, declarationValid: true, resident: true,
            qualifyingUse: true, trading: false));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
    }

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public void Calculate_DoesNotApplyInvalidOrNonQualifyingDeclaration(
        bool valid, bool resident, bool qualifyingUse, bool trading)
    {
        var result = _calculator.Calculate(Request(
            declaration: true, declarationValid: valid, resident: resident,
            qualifyingUse: qualifyingUse, trading: trading));
        Assert.Equal(TdsCalculationStatus.ReviewRequired, result.Status);
    }

    [Fact]
    public void Calculate_RejectsOtherForestProduceNotObtainedUnderLeaseFromApril2025()
    {
        var result = _calculator.Calculate(Request(
            Tcs206C1GoodsCategory.OtherForestProduceUnderForestLease,
            receipt: new DateOnly(2025, 4, 1), forestLease: false));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
    }

    [Fact]
    public void Calculate_UsesLowerRateCertificateOnlyWhenValidated()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(
            certificate: true, certificateValid: false, certificateRate: 0.5m)).Status);
        var result = _calculator.Calculate(Request(
            certificate: true, certificateValid: true, certificateRate: 0.5m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(0.5m, result.AppliedRatePercent);
        Assert.Equal(500m, result.TcsAmount);
    }

    [Fact]
    public void Calculate_HasNoMonetaryThresholdAndTaxesCurrentEventOnly()
    {
        var result = _calculator.Calculate(Request(current: 1m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(1m, result.TaxableAmount);
        Assert.Equal(0.02m, result.TcsAmount);
    }

    [Fact]
    public void Calculate_RejectsMissingDatesAndNegativeAmount()
    {
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request(omitDates: true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(current: -1m)));
    }
}
