using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tds194ADeductionCalculatorTests
{
    private readonly Tds194ADeductionCalculator _calculator = new();

    private static Tds194ADeductionRequest Request(
        DateOnly? credit = null,
        DateOnly? payment = null,
        decimal amount = 10000m,
        decimal prior = 0m,
        Tds194APayerCategory payer = Tds194APayerCategory.OtherSpecifiedPayer,
        bool resident = true,
        bool senior = false,
        bool pan = true,
        bool confirmed = true,
        bool securities = false,
        bool exempt = false,
        bool qualifyingDeposit = true,
        bool turnoverConfirmed = true,
        bool auditLiable = true,
        bool suspense = false,
        decimal? certificateRate = null,
        string? certificateRef = null,
        DateOnly? validFrom = null,
        DateOnly? validTo = null,
        bool omitCredit = false) =>
        new(omitCredit ? null : credit ?? new DateOnly(2026, 4, 1), payment,
            amount, prior, payer, resident, senior, pan, confirmed, securities, exempt,
            qualifyingDeposit, turnoverConfirmed, auditLiable, suspense,
            certificateRate, certificateRef, validFrom, validTo);

    [Fact]
    public void Calculate_UsesEarlierCreditOrPaymentForActTransition()
    {
        var result = _calculator.Calculate(Request(
            credit: new(2026, 4, 2), payment: new(2026, 3, 31)));
        Assert.Equal("Income-tax Act, 1961", result.GoverningAct);
        Assert.Equal("Section 194A", result.SectionReference);
    }

    [Fact]
    public void Calculate_UsesNewActTableEntryForOtherSpecifiedPayer()
    {
        var result = _calculator.Calculate(Request());
        Assert.Equal("Income-tax Act, 2025", result.GoverningAct);
        Assert.Equal("Section 393(1), Table Sl. No. 5(iii)", result.SectionReference);
        Assert.Equal(10m, result.AppliedRatePercent);
        Assert.Equal(1000m, result.TdsAmount);
    }

    [Theory]
    [InlineData(2024, 4, 1, 5000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(2025, 4, 1, 10000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(2025, 4, 1, 10001, TdsCalculationStatus.Calculated)]
    [InlineData(2024, 4, 1, 5001, TdsCalculationStatus.Calculated)]
    public void Calculate_AppliesEffectiveDatedOtherPayerThreshold(
        int year, int month, int day, decimal amount, TdsCalculationStatus expected)
    {
        var result = _calculator.Calculate(Request(
            credit: new DateOnly(year, month, day), amount: amount));
        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData(false, 50000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(false, 50001, TdsCalculationStatus.Calculated)]
    [InlineData(true, 100000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(true, 100001, TdsCalculationStatus.Calculated)]
    public void Calculate_AppliesBankThresholdBySeniorCitizenStatus(
        bool senior, decimal amount, TdsCalculationStatus expected)
    {
        var result = _calculator.Calculate(Request(
            payer: Tds194APayerCategory.EligibleBankingOrPostOfficeDeposit,
            senior: senior, amount: amount));
        Assert.Equal(expected, result.Status);
        if (expected == TdsCalculationStatus.Calculated)
            Assert.Equal("Section 393(1), Table Sl. No. 5(ii)", result.SectionReference);
    }

    [Fact]
    public void Calculate_UsesEntireCurrentPaymentOnceThresholdIsCrossed()
    {
        var result = _calculator.Calculate(Request(amount: 2000m, prior: 9000m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(2000m, result.TaxableAmount);
        Assert.Equal(200m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_ExcludesNonResidentSecuritiesAndSpecificExemptions()
    {
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(resident: false)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(securities: true)).Status);
        Assert.Equal(TdsCalculationStatus.BelowThreshold, _calculator.Calculate(Request(exempt: true)).Status);
    }

    [Fact]
    public void Calculate_RequiresReviewForMissingPanAndUnconfirmedApplicability()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(pan: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(confirmed: false)).Status);
    }

    [Fact]
    public void Calculate_RequiresReviewForUnqualifiedBankDepositClassification()
    {
        var result = _calculator.Calculate(Request(
            payer: Tds194APayerCategory.EligibleBankingOrPostOfficeDeposit,
            qualifyingDeposit: false));
        Assert.Equal(TdsCalculationStatus.ReviewRequired, result.Status);
    }

    [Fact]
    public void Calculate_HandlesIndividualHufPayerThreshold()
    {
        Assert.Equal(TdsCalculationStatus.BelowThreshold,
            _calculator.Calculate(Request(payer: Tds194APayerCategory.IndividualOrHuf,
                turnoverConfirmed: true, auditLiable: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(payer: Tds194APayerCategory.IndividualOrHuf,
                turnoverConfirmed: false)).Status);
    }

    [Fact]
    public void Calculate_ValidatesLowerDeductionCertificate()
    {
        var result = _calculator.Calculate(Request(certificateRate: 5m,
            certificateRef: "CERT-194A", validFrom: new(2026, 4, 1),
            validTo: new(2027, 3, 31)));
        Assert.Equal(5m, result.AppliedRatePercent);
        Assert.Equal(500m, result.TdsAmount);

        var invalid = _calculator.Calculate(Request(certificateRate: 12m,
            certificateRef: "CERT-194A", validFrom: new(2026, 4, 1),
            validTo: new(2027, 3, 31)));
        Assert.Equal(TdsCalculationStatus.ReviewRequired, invalid.Status);
    }

    [Fact]
    public void Calculate_UsesPaymentWhenCreditDateIsMissingAndFlagsSuspenseCredit()
    {
        var result = _calculator.Calculate(Request(payment: new(2026, 4, 1),
            omitCredit: true, suspense: true));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Contains("interest payable/suspense account", result.Explanation);
    }

    [Fact]
    public void Calculate_RejectsNegativeAmountsAndMissingDates()
    {
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request(
            payment: null, omitCredit: true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(amount: -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(prior: -1m)));
    }
}
