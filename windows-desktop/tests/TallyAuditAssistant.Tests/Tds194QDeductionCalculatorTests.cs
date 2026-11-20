using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tds194QDeductionCalculatorTests
{
    private readonly Tds194QDeductionCalculator _calculator = new();

    private static Tds194QDeductionRequest Request(
        DateOnly? credit = null,
        DateOnly? payment = null,
        decimal gross = 6000000m,
        decimal gst = 0m,
        decimal prior = 0m,
        decimal turnover = 120000000m,
        bool buyerBusiness = true,
        bool resident = true,
        bool sellerExempt = false,
        bool goods = true,
        bool pan = true,
        bool applicability = true,
        bool otherTds = false,
        bool ecommerceTds = false,
        bool otherTcs = false,
        bool tcs206c1h = false) =>
        new(credit ?? new DateOnly(2026, 4, 1), payment, gross, gst, prior, turnover,
            buyerBusiness, resident, sellerExempt, goods, pan, applicability,
            otherTds, ecommerceTds, otherTcs, tcs206c1h);

    [Fact]
    public void Calculate_UsesEarlierEventForActTransition()
    {
        var result = _calculator.Calculate(Request(credit: new(2026, 3, 31), payment: new(2026, 4, 2)));
        Assert.Equal("Income-tax Act, 1961", result.GoverningAct);
        Assert.Equal("Section 194Q", result.SectionReference);
    }

    [Fact]
    public void Calculate_UsesNewActTableEntryFromApril2026()
    {
        var result = _calculator.Calculate(Request());
        Assert.Equal("Income-tax Act, 2025", result.GoverningAct);
        Assert.Equal("Section 393(1), Table Sl. No. 8(ii)", result.SectionReference);
    }

    [Theory]
    [InlineData(100000000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(100000001, TdsCalculationStatus.Calculated)]
    public void Calculate_RequiresBuyerTurnoverToExceedTenCrore(decimal turnover, TdsCalculationStatus expected)
    {
        var result = _calculator.Calculate(Request(turnover: turnover));
        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData(5000000, 0, TdsCalculationStatus.BelowThreshold, 0)]
    [InlineData(5001000, 0, TdsCalculationStatus.Calculated, 1)]
    [InlineData(1000000, 4900000, TdsCalculationStatus.Calculated, 900)]
    [InlineData(1000000, 5000000, TdsCalculationStatus.Calculated, 1000)]
    public void Calculate_DeductsOnlyIncrementalAmountAboveSellerThreshold(
        decimal amount, decimal prior, TdsCalculationStatus expected, decimal expectedTax)
    {
        var result = _calculator.Calculate(Request(gross: amount, prior: prior));
        Assert.Equal(expected, result.Status);
        Assert.Equal(expectedTax, result.TdsAmount);
        if (expected == TdsCalculationStatus.Calculated)
            Assert.Equal(expectedTax / 0.001m, result.TaxableAmount);
    }

    [Fact]
    public void Calculate_ExcludesSeparatelyStatedGstWhenCreditIsEarlier()
    {
        var result = _calculator.Calculate(Request(gross: 6000000m, gst: 600000m,
            credit: new(2026, 4, 1), payment: new(2026, 4, 5)));
        Assert.Equal(500000m, result.TaxableAmount);
        Assert.Equal(500m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_IncludesFullPaymentWhenPaymentIsEarlierThanCredit()
    {
        var result = _calculator.Calculate(Request(gross: 6000000m, gst: 600000m,
            credit: new(2026, 4, 5), payment: new(2026, 4, 1)));
        Assert.Equal(1000000m, result.TaxableAmount);
        Assert.Equal(1000m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_IncludesAdvancePaymentWhenNoCreditDateExists()
    {
        var result = _calculator.Calculate(Request(credit: new(2026, 4, 5), payment: new(2026, 4, 1), gross: 6000000m, gst: 600000m));
        Assert.Equal(1000000m, result.TaxableAmount);
        Assert.Equal(1000m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_RequiresReviewForMissingPanOrUnconfirmedApplicability()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(pan: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(applicability: false)).Status);
    }

    [Fact]
    public void Calculate_RespectsSellerAndTransactionExclusions()
    {
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(resident: false)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(sellerExempt: true)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(otherTds: true)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(ecommerceTds: true)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(tcs206c1h: true)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(otherTcs: true)).Status);
    }

    [Fact]
    public void Calculate_RejectsInvalidAmountsAndMissingDates()
    {
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request(credit: null, payment: null)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(gross: -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(gross: 100m, gst: 101m)));
    }
}
