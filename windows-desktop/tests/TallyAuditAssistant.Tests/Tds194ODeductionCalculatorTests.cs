using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tds194ODeductionCalculatorTests
{
    private readonly Tds194ODeductionCalculator _calculator = new();

    private static Tds194ODeductionRequest Request(
        DateOnly? credit = null,
        DateOnly? payment = null,
        decimal gross = 100000m,
        decimal prior = 0m,
        TdsDeducteeEntityType entity = TdsDeducteeEntityType.Other,
        bool resident = true,
        bool panOrAadhaar = true,
        bool confirmed = true,
        bool directPaymentsIncluded = true,
        bool advertising = false,
        bool vda = false,
        decimal? certificateRate = null,
        string? certificateRef = null,
        DateOnly? validFrom = null,
        DateOnly? validTo = null,
        bool omitCreditDate = false) =>
        new(omitCreditDate ? null : credit ?? new DateOnly(2026, 4, 1), payment,
            gross, prior, entity, resident, panOrAadhaar, confirmed, directPaymentsIncluded,
            advertising, vda, certificateRate, certificateRef, validFrom, validTo);

    [Fact]
    public void Calculate_UsesEarlierCreditOrPaymentForActTransition()
    {
        var result = _calculator.Calculate(Request(credit: new(2026, 3, 31), payment: new(2026, 4, 2)));
        Assert.Equal("Income-tax Act, 1961", result.GoverningAct);
        Assert.Equal("Section 194-O", result.SectionReference);
    }

    [Fact]
    public void Calculate_UsesNewActTableEntryFromApril2026()
    {
        var result = _calculator.Calculate(Request());
        Assert.Equal("Income-tax Act, 2025", result.GoverningAct);
        Assert.Equal("Section 393(1), Table Sl. No. 8(v)", result.SectionReference);
        Assert.Equal(0.1m, result.AppliedRatePercent);
        Assert.Equal(100m, result.TdsAmount);
    }

    [Theory]
    [InlineData(500000, 0, true, TdsCalculationStatus.BelowThreshold)]
    [InlineData(500001, 0, true, TdsCalculationStatus.Calculated)]
    [InlineData(100000, 400000, true, TdsCalculationStatus.BelowThreshold)]
    [InlineData(100000, 400001, true, TdsCalculationStatus.Calculated)]
    public void Calculate_AppliesFiveLakhExemptionOnlyToIndividualHufWithPanOrAadhaar(
        decimal gross, decimal prior, bool identityProvided, TdsCalculationStatus expected)
    {
        var result = _calculator.Calculate(Request(gross: gross, prior: prior,
            entity: TdsDeducteeEntityType.IndividualOrHuf, panOrAadhaar: identityProvided));
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Calculate_DoesNotApplyFiveLakhExemptionToCompaniesOrOtherEntities()
    {
        var result = _calculator.Calculate(Request(gross: 1000m,
            entity: TdsDeducteeEntityType.Other));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(1m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_UsesFullCurrentGrossAmountOnceAnnualExemptionIsCrossed()
    {
        var result = _calculator.Calculate(Request(gross: 100000m, prior: 450000m,
            entity: TdsDeducteeEntityType.IndividualOrHuf));
        Assert.Equal(100000m, result.TaxableAmount);
        Assert.Equal(100m, result.TdsAmount);
        Assert.Contains("not merely the amount exceeding ₹5 lakh", result.Explanation);
    }

    [Fact]
    public void Calculate_RequiresReviewForMissingIdentityOrUnconfirmedPlatformApplicability()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(panOrAadhaar: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(confirmed: false)).Status);
    }

    [Fact]
    public void Calculate_ExcludesNonResidentAdvertisingAndVirtualDigitalAssetTransactions()
    {
        Assert.Equal(TdsCalculationStatus.BelowThreshold,
            _calculator.Calculate(Request(resident: false)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold,
            _calculator.Calculate(Request(advertising: true)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold,
            _calculator.Calculate(Request(vda: true)).Status);
    }

    [Fact]
    public void Calculate_AppliesValidLowerDeductionCertificate()
    {
        var result = _calculator.Calculate(Request(certificateRate: 0.05m,
            certificateRef: "CERT-194O", validFrom: new(2026, 4, 1),
            validTo: new(2027, 3, 31)));
        Assert.Equal(0.05m, result.AppliedRatePercent);
        Assert.Equal(50m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_UsesAdvancePaymentWhenCreditDateIsAbsent()
    {
        var result = _calculator.Calculate(Request(payment: new(2026, 4, 1),
            gross: 100000m, omitCreditDate: true));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal("Income-tax Act, 2025", result.GoverningAct);
    }

    [Fact]
    public void Calculate_RejectsInvalidAmountsAndMissingDates()
    {
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request(
            payment: null, omitCreditDate: true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(gross: -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(prior: -1m)));
    }
}
