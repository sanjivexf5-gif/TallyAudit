using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tcs206C1CLeaseCalculatorTests
{
    private readonly Tcs206C1CLeaseCalculator _calculator = new();

    private static Tcs206C1CLeaseRequest Request(
        Tcs206C1CContractType type = Tcs206C1CContractType.ParkingLot,
        DateOnly? receipt = null, DateOnly? debit = null,
        decimal amount = 100000m, bool grantor = true, bool licenseeChecked = true,
        bool psu = false, bool business = true, bool mineralOil = false,
        bool pan = true, bool certificate = false, bool certificateValid = false,
        decimal? certificateRate = null, bool omitDates = false) =>
        new(type, omitDates ? receipt : receipt ?? new DateOnly(2026, 4, 1),
            debit, amount, grantor, licenseeChecked, psu, business, mineralOil,
            pan, certificate, certificateValid, certificateRate);

    [Theory]
    [InlineData(Tcs206C1CContractType.ParkingLot)]
    [InlineData(Tcs206C1CContractType.TollPlaza)]
    [InlineData(Tcs206C1CContractType.Mine)]
    [InlineData(Tcs206C1CContractType.Quarry)]
    public void Calculate_AppliesTwoPercentToEveryCoveredRight(Tcs206C1CContractType type)
    {
        var result = _calculator.Calculate(Request(type));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(2m, result.AppliedRatePercent);
        Assert.Equal(2000m, result.TcsAmount);
        Assert.Equal(100000m, result.TaxableAmount);
    }

    [Fact]
    public void Calculate_UsesOldAndNewActBasedOnEarlierDebitOrReceipt()
    {
        var oldAct = _calculator.Calculate(Request(receipt: new DateOnly(2026, 4, 5),
            debit: new DateOnly(2026, 3, 31)));
        var newAct = _calculator.Calculate(Request(receipt: new DateOnly(2026, 4, 1)));
        Assert.Equal("Income-tax Act, 1961", oldAct.GoverningAct);
        Assert.Equal("Section 206C(1C)", oldAct.SectionReference);
        Assert.Equal("Income-tax Act, 2025", newAct.GoverningAct);
        Assert.Equal("Section 394(1), Table Sl. No. 9", newAct.SectionReference);
    }

    [Fact]
    public void Calculate_RequiresGrantorAndLicenseeEligibilityReview()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(grantor: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(licenseeChecked: false)).Status);
    }

    [Fact]
    public void Calculate_ExcludesPublicSectorCompanyLicensee()
    {
        var result = _calculator.Calculate(Request(psu: true));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
    }

    [Fact]
    public void Calculate_RequiresBusinessUse()
    {
        var result = _calculator.Calculate(Request(business: false));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
    }

    [Theory]
    [InlineData(Tcs206C1CContractType.Mine)]
    [InlineData(Tcs206C1CContractType.Quarry)]
    public void Calculate_ExcludesMineralOilMiningAndQuarrying(Tcs206C1CContractType type)
    {
        var result = _calculator.Calculate(Request(type, mineralOil: true));
        Assert.Equal(TdsCalculationStatus.BelowThreshold, result.Status);
    }

    [Fact]
    public void Calculate_DoesNotExcludeParkingOrTollForMineralOilFlag()
    {
        var result = _calculator.Calculate(Request(Tcs206C1CContractType.ParkingLot,
            mineralOil: true));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
    }

    [Fact]
    public void Calculate_RequiresPanRatherThanAssumingHigherRate()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(pan: false)).Status);
    }

    [Fact]
    public void Calculate_AppliesOnlyVerifiedLowerRateCertificate()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(certificate: true, certificateValid: false,
                certificateRate: 0.5m)).Status);
        var result = _calculator.Calculate(Request(certificate: true,
            certificateValid: true, certificateRate: 0.5m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(0.5m, result.AppliedRatePercent);
        Assert.Equal(500m, result.TcsAmount);
    }

    [Fact]
    public void Calculate_HasNoThresholdAndUsesOnlyCurrentEventAmount()
    {
        var result = _calculator.Calculate(Request(amount: 1m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(1m, result.TaxableAmount);
        Assert.Equal(0.02m, result.TcsAmount);
    }

    [Fact]
    public void Calculate_RejectsMissingDatesAndNegativeAmount()
    {
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request(omitDates: true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(amount: -1m)));
    }
}
