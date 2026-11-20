using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tcs206C1HHistoricalValidatorTests
{
    private readonly Tcs206C1HHistoricalValidator _validator = new();

    private static Tcs206C1HHistoricalRequest Request(
        DateOnly? date = null,
        decimal prior = 0m,
        decimal current = 100000m,
        bool seller = true,
        bool turnover = true,
        bool buyerChecked = true,
        bool excluded = false,
        bool exported = false,
        bool coveredElsewhere = false,
        bool liableOtherTds = false,
        bool deductedOtherTds = false,
        bool pan = true,
        bool certificate = false,
        bool certificateValid = false,
        decimal? certificateRate = null,
        bool omitDate = false) =>
        new(omitDate ? null : date ?? new DateOnly(2024, 10, 1),
            prior, current, seller, turnover, buyerChecked, excluded,
            exported, coveredElsewhere, liableOtherTds, deductedOtherTds,
            pan, certificate, certificateValid, certificateRate);

    [Theory]
    [InlineData(2020, 9, 30)]
    [InlineData(2025, 4, 1)]
    [InlineData(2026, 4, 1)]
    public void Validate_RejectsDatesOutsideHistoricalApplicability(int year, int month, int day)
    {
        var result = _validator.Validate(Request(date: new DateOnly(year, month, day)));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
        Assert.Equal(0m, result.TcsAmount);
        Assert.Contains("not applicable", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_AppliesFromFirstOctober2020()
    {
        var result = _validator.Validate(Request(date: new DateOnly(2020, 10, 1), prior: 5000000m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(0.1m, result.AppliedRatePercent);
    }

    [Fact]
    public void Validate_RequiresSellerTurnoverExceedingTenCroreInPriorYear()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _validator.Validate(Request(seller: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _validator.Validate(Request(turnover: false)).Status);
    }

    [Fact]
    public void Validate_ThresholdIsBuyerWiseAndCurrentReceiptOnlyIsTaxedAboveThreshold()
    {
        var result = _validator.Validate(Request(prior: 4900000m, current: 200000m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(100000m, result.TaxableAmount);
        Assert.Equal(100m, result.TcsAmount);
    }

    [Fact]
    public void Validate_DoesNotCollectWhenAggregateReceiptsDoNotExceedFiftyLakh()
    {
        var result = _validator.Validate(Request(prior: 4000000m, current: 1000000m));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
        Assert.Equal(0m, result.TcsAmount);
    }

    [Fact]
    public void Validate_TaxesEntireCurrentReceiptWhenThresholdWasAlreadyCrossed()
    {
        var result = _validator.Validate(Request(prior: 6000000m, current: 250000m));
        Assert.Equal(250000m, result.TaxableAmount);
        Assert.Equal(250m, result.TcsAmount);
    }

    [Fact]
    public void Validate_UsesOnePercentWhenPanIsNotProvided()
    {
        var result = _validator.Validate(Request(prior: 5000000m, current: 100000m, pan: false));
        Assert.Equal(1m, result.AppliedRatePercent);
        Assert.Equal(1000m, result.TcsAmount);
    }

    [Fact]
    public void Validate_RecognisesStatutoryBuyerAndGoodsExclusions()
    {
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _validator.Validate(Request(excluded: true)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _validator.Validate(Request(exported: true)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _validator.Validate(Request(coveredElsewhere: true)).Status);
    }

    [Fact]
    public void Validate_RequiresProofOfOtherTdsDeductionForNoTcsException()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _validator.Validate(Request(liableOtherTds: true, deductedOtherTds: false)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold,
            _validator.Validate(Request(liableOtherTds: true, deductedOtherTds: true)).Status);
    }

    [Fact]
    public void Validate_UsesOnlyVerifiedLowerRateCertificate()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _validator.Validate(Request(prior: 5000000m, certificate: true, certificateValid: false, certificateRate: 0.05m)).Status);
        var result = _validator.Validate(Request(prior: 5000000m, certificate: true, certificateValid: true, certificateRate: 0.05m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(0.05m, result.AppliedRatePercent);
        Assert.Equal(50m, result.TcsAmount);
    }

    [Fact]
    public void Validate_RequiresReceiptDateAndRejectsNegativeAmounts()
    {
        Assert.Throws<ArgumentException>(() => _validator.Validate(Request(omitDate: true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _validator.Validate(Request(current: -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _validator.Validate(Request(prior: -1m)));
    }
}
