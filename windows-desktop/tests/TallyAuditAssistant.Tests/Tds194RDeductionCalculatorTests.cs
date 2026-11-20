using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tds194RDeductionCalculatorTests
{
    private readonly Tds194RDeductionCalculator _calculator = new();

    private static Tds194RDeductionRequest Request(
        DateOnly? date = null,
        bool omitDate = false,
        decimal amount = 25000m,
        decimal prior = 0m,
        bool resident = true,
        bool business = true,
        bool confirmed = true,
        bool pan = true,
        bool excludesGst = true,
        bool inKind = false,
        decimal cash = 0m,
        bool taxPaid = false,
        bool excluded = false,
        bool individualProvider = false,
        bool turnoverConfirmed = true,
        bool withinThreshold = true,
        decimal? certificateRate = null,
        string? certificateRef = null,
        DateOnly? validFrom = null,
        DateOnly? validTo = null) =>
        new(omitDate ? null : date ?? new DateOnly(2026, 4, 1), amount, prior, resident, business,
            confirmed, pan, excludesGst, inKind, cash, taxPaid, excluded,
            individualProvider, turnoverConfirmed, withinThreshold,
            certificateRate, certificateRef, validFrom, validTo);

    [Fact]
    public void Calculate_UsesOldActBeforeApril2026AndNewActFromApril2026()
    {
        var oldResult = _calculator.Calculate(Request(date: new(2026, 3, 31)));
        var newResult = _calculator.Calculate(Request(date: new(2026, 4, 1)));
        Assert.Equal("Income-tax Act, 1961", oldResult.GoverningAct);
        Assert.Equal("Section 194R", oldResult.SectionReference);
        Assert.Equal("Income-tax Act, 2025", newResult.GoverningAct);
        Assert.Equal("Section 393(1), Table Sl. No. 8(iv)", newResult.SectionReference);
    }

    [Theory]
    [InlineData(20000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(20001, TdsCalculationStatus.Calculated)]
    public void Calculate_AppliesStrictTwentyThousandThreshold(decimal amount, TdsCalculationStatus expected)
    {
        Assert.Equal(expected, _calculator.Calculate(Request(amount: amount, cash: amount)).Status);
    }

    [Fact]
    public void Calculate_AppliesTdsToFullAggregateNotOnlyExcess()
    {
        var result = _calculator.Calculate(Request(amount: 5000m, prior: 18000m, cash: 2300m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(23000m, result.TaxableAmount);
        Assert.Equal(2300m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_RequiresResidentBusinessOrProfessionRecipient()
    {
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(resident: false)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(business: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(confirmed: false)).Status);
    }

    [Fact]
    public void Calculate_HandlesIndividualHufProviderThreshold()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(individualProvider: true, turnoverConfirmed: false)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold,
            _calculator.Calculate(Request(individualProvider: true, withinThreshold: false)).Status);
        Assert.Equal(TdsCalculationStatus.Calculated,
            _calculator.Calculate(Request(individualProvider: true, cash: 2500m)).Status);
    }

    [Fact]
    public void Calculate_RequiresGstExcludedValueAndHonoursGuidanceExclusion()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(excludesGst: false)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold,
            _calculator.Calculate(Request(excluded: true)).Status);
    }

    [Fact]
    public void Calculate_RequiresTaxPaymentEvidenceForWhollyInKindBenefit()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(inKind: true)).Status);
        var paid = _calculator.Calculate(Request(inKind: true, taxPaid: true));
        Assert.Equal(TdsCalculationStatus.Calculated, paid.Status);
        Assert.Contains("challan evidence", paid.Explanation);
    }

    [Fact]
    public void Calculate_RequiresTaxPaymentWhenCashIsInsufficient()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(cash: 100m)));
        Assert.Equal(TdsCalculationStatus.Calculated,
            _calculator.Calculate(Request(cash: 2500m)));
    }

    [Fact]
    public void Calculate_RequiresReviewForMissingPanAndValidatesCertificate()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(pan: false, cash: 2500m)));
        var valid = _calculator.Calculate(Request(cash: 2500m, certificateRate: 5m,
            certificateRef: "CERT-194R", validFrom: new(2026, 4, 1), validTo: new(2027, 3, 31)));
        Assert.Equal(5m, valid.AppliedRatePercent);
        Assert.Equal(1250m, valid.TdsAmount);
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(cash: 2500m, certificateRate: 12m,
                certificateRef: "CERT-194R", validFrom: new(2026, 4, 1), validTo: new(2027, 3, 31))).Status);
    }

    [Fact]
    public void Calculate_RejectsInvalidValuesAndMissingDate()
    {
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request(omitDate: true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(amount: -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(prior: -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(cash: -1m)));
    }
}
