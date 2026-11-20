using TallyAuditAssistant.Engine.Tds.Calculation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Tds194SDeductionCalculatorTests
{
    private readonly Tds194SDeductionCalculator _calculator = new();

    private static Tds194SDeductionRequest Request(
        DateOnly? date = null,
        bool omitDate = false,
        decimal amount = 25000m,
        decimal prior = 0m,
        bool vda = true,
        bool resident = true,
        bool confirmed = true,
        bool individualOrHuf = false,
        bool specifiedConfirmed = true,
        bool specified = false,
        bool pan = true,
        bool inKind = false,
        decimal cashAvailable = 0m,
        bool taxPaidBeforeRelease = false,
        bool exchangeDeductor = false,
        bool alreadyDeducted = false,
        decimal? certificateRate = null,
        string? certificateRef = null,
        DateOnly? validFrom = null,
        DateOnly? validTo = null) =>
        new(omitDate ? null : date ?? new DateOnly(2025, 4, 1), amount, prior,
            vda, resident, confirmed, individualOrHuf, specifiedConfirmed,
            specified, pan, inKind, cashAvailable, taxPaidBeforeRelease,
            exchangeDeductor, alreadyDeducted, certificateRate, certificateRef,
            validFrom, validTo);

    [Fact]
    public void Calculate_UsesOldAndNewActOnCorrectDates()
    {
        var beforeEffective = _calculator.Calculate(Request(date: new(2022, 6, 30)));
        var oldAct = _calculator.Calculate(Request(date: new(2026, 3, 31)));
        var newAct = _calculator.Calculate(Request(date: new(2026, 4, 1)));

        Assert.Equal(TdsCalculationStatus.BelowThreshold, beforeEffective.Status);
        Assert.Equal("Section 194S", oldAct.SectionReference);
        Assert.Equal("Income-tax Act, 1961", oldAct.GoverningAct);
        Assert.Equal("Section 393(1), Table Sl. No. 8(vi)", newAct.SectionReference);
        Assert.Equal("Income-tax Act, 2025", newAct.GoverningAct);
    }

    [Theory]
    [InlineData(10000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(10001, TdsCalculationStatus.Calculated)]
    public void Calculate_UsesTenThousandThresholdForOtherPayers(decimal aggregate, TdsCalculationStatus expected)
    {
        var result = _calculator.Calculate(Request(amount: aggregate));
        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData(50000, TdsCalculationStatus.BelowThreshold)]
    [InlineData(50001, TdsCalculationStatus.Calculated)]
    public void Calculate_UsesFiftyThousandThresholdForSpecifiedPersons(decimal aggregate, TdsCalculationStatus expected)
    {
        var result = _calculator.Calculate(Request(amount: aggregate,
            individualOrHuf: true, specified: true));
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Calculate_TaxesOnlyCurrentTransactionAfterAnnualAggregateCrossesThreshold()
    {
        var result = _calculator.Calculate(Request(amount: 2000m, prior: 9000m));
        Assert.Equal(TdsCalculationStatus.Calculated, result.Status);
        Assert.Equal(2000m, result.TaxableAmount);
        Assert.Equal(20m, result.TdsAmount);
    }

    [Fact]
    public void Calculate_RequiresSpecifiedPersonEvidenceForIndividualOrHuf()
    {
        var result = _calculator.Calculate(Request(amount: 50001m,
            individualOrHuf: true, specifiedConfirmed: false));
        Assert.Equal(TdsCalculationStatus.ReviewRequired, result.Status);
    }

    [Fact]
    public void Calculate_RequiresVdaClassificationResidentPayeeAndApplicability()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(vda: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(resident: false)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(confirmed: false)).Status);
    }

    [Fact]
    public void Calculate_RequiresTaxPaymentBeforeReleaseWhenInKindCashIsInsufficient()
    {
        var unresolved = _calculator.Calculate(Request(amount: 25000m, inKind: true, cashAvailable: 100m));
        var resolved = _calculator.Calculate(Request(amount: 25000m, inKind: true,
            cashAvailable: 100m, taxPaidBeforeRelease: true));

        Assert.Equal(TdsCalculationStatus.ReviewRequired, unresolved.Status);
        Assert.Equal(TdsCalculationStatus.Calculated, resolved.Status);
        Assert.Equal(250m, resolved.TdsAmount);
    }

    [Fact]
    public void Calculate_PreventsDuplicateDeductionAndFlagsExchangeAlternative()
    {
        Assert.Equal(TdsCalculationStatus.BelowThreshold,
            _calculator.Calculate(Request(alreadyDeducted: true)).Status);
        Assert.Equal(TdsCalculationStatus.ReviewRequired,
            _calculator.Calculate(Request(exchangeDeductor: true)).Status);
    }

    [Fact]
    public void Calculate_ValidatesPanCertificateAndRequiredAmounts()
    {
        Assert.Equal(TdsCalculationStatus.ReviewRequired, _calculator.Calculate(Request(pan: false)).Status);
        var valid = _calculator.Calculate(Request(certificateRate: 0.5m,
            certificateRef: "CERT-194S", validFrom: new(2025, 4, 1), validTo: new(2026, 3, 31)));
        Assert.Equal(TdsCalculationStatus.Calculated, valid.Status);
        Assert.Equal(0.5m, valid.AppliedRatePercent);
        Assert.Equal(125m, valid.TdsAmount);

        Assert.Throws<ArgumentException>(() => _calculator.Calculate(Request(omitDate: true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(amount: -1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calculator.Calculate(Request(prior: -1m)));
    }
}
