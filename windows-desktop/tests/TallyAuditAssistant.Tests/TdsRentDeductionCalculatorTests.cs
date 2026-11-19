using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class TdsRentDeductionCalculatorTests
{
    private readonly TdsRentDeductionCalculator _calculator = new();

    private static TdsRentDeductionRequest Request(
        DateOnly? credit = null,
        DateOnly? payment = null,
        decimal rent = 60000m,
        TdsRentAssetCategory asset = TdsRentAssetCategory.LandBuildingOrAppurtenant,
        bool individualOrHuf = false,
        bool specified = true,
        bool pan = true,
        bool applicability = true,
        bool finalMonth = true,
        decimal? certificateRate = null,
        string? certificateRef = null,
        DateOnly? validFrom = null,
        DateOnly? validTo = null) =>
        new(credit ?? new DateOnly(2026, 4, 1), payment, rent, asset, individualOrHuf,
            specified, pan, applicability, finalMonth, certificateRate, certificateRef, validFrom, validTo);

    [Fact]
    public void Calculate_UsesEarlierEventForActTransition()
    {
        var result = _calculator.Calculate(Request(credit: new(2026, 3, 31), payment: new(2026, 4, 2)));
        Assert.Equal("Income-tax Act, 1961", result.GoverningAct);
        Assert.Equal("Section 194-I", result.SectionReference);
        Assert.Equal(10m, result.AppliedRatePercent);
    }

    [Theory]
    [InlineData(TdsRentAssetCategory.LandBuildingOrAppurtenant, 10)]
    [InlineData(TdsRentAssetCategory.MachineryPlantOrEquipment, 2)]
    [InlineData(TdsRentAssetCategory.FurnitureOrFittings, 10)]
    public void Calculate_SpecifiedPayerUsesAssetSpecificRates(TdsRentAssetCategory asset, decimal rate)
    {
        var result = _calculator.Calculate(Request(asset: asset));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(rate, result.AppliedRatePercent);
    }

    [Fact]
    public void Calculate_NonSpecifiedIndividualHufUsesOldAct194IbAndDefersToFinalMonth()
    {
        var notFinal = _calculator.Calculate(Request(credit: new(2026, 3, 1), individualOrHuf: true,
            specified: false, finalMonth: false));
        Assert.Equal("Section 194-IB", notFinal.SectionReference);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, notFinal.Status);

        var final = _calculator.Calculate(Request(credit: new(2026, 3, 1), individualOrHuf: true,
            specified: false, finalMonth: true));
        Assert.Equal(TdsCalculationStatus.Calculated, final.Status);
        Assert.Equal(2m, final.AppliedRatePercent);
    }

    [Fact]
    public void Calculate_NewActNonSpecifiedPayerUsesTable2IITwoPercent()
    {
        var result = _calculator.Calculate(Request(individualOrHuf: true, specified: false));
        Assert.Equal("Section 393(1), Table Sl. No. 2(i)", result.SectionReference);
        Assert.Equal(2m, result.AppliedRatePercent);
        Assert.Equal(1200m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_NonSpecifiedPayerCannotUseMachineryEntry()
    {
        var result = _calculator.Calculate(Request(asset: TdsRentAssetCategory.MachineryPlantOrEquipment,
            individualOrHuf: true, specified: false));
        Assert.Equal(TdsCalculationStatus.ReviewRequired, result.Status);
    }

    [Theory]
    [InlineData(50000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(50001, TdsCalculationStatus.Calculated)]
    public void Calculate_UsesMonthlyThreshold(decimal rent, TdsCalculationStatus expected)
    {
        var result = _calculator.Calculate(Request(rent: rent));
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Calculate_RequiresReviewForMissingPanOrUnconfirmedApplicability()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(pan: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(applicability: false)).Status);
    }

    [Fact]
    public void Calculate_AppliesValidLowerDeductionCertificate()
    {
        var result = _calculator.Calculate(Request(certificateRate: 5m, certificateRef: "CERT-1",
            validFrom: new(2026, 4, 1), validTo: new(2027, 3, 31)));
        Assert.Equal(5m, result.AppliedRatePercent);
        Assert.Equal(3000m, result.TdsAmount);
    }
}
