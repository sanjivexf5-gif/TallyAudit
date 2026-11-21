using System;
using TallyAuditAssistant.Engine.Tds.Returns;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class TcsBuyerReturnReliefAssessmentServiceTests
{
    private readonly TcsBuyerReturnReliefAssessmentService _service = new();

    [Fact]
    public void Assess_QualifyingOldActSection206C1_UsesBuyerReturnDateAsInterestEnd()
    {
        var result = _service.Assess(new TcsBuyerReturnReliefRequest(
            TcsBuyerReturnReliefRegime.IncomeTaxAct1961FromApril2025,
            TcsBuyerReturnReliefCollectionCategory.Section206C1SpecifiedGoodsOrNewActTable1To5,
            new DateOnly(2025, 4, 10), new DateOnly(2025, 8, 20),
            true, true, true, false));

        Assert.Equal(TcsBuyerReturnReliefStatus.EligibleForRelief, result.Status);
        Assert.True(result.CollectorNotDeemedInDefault);
        Assert.Equal(new DateOnly(2025, 8, 20), result.InterestEndDate);
        Assert.Equal(1.0m, result.InterestRatePercent);
        Assert.Contains("206C(7)", result.SectionReference);
    }

    [Fact]
    public void Assess_QualifyingOldActSection206C1C_IsCovered()
    {
        var result = _service.Assess(new TcsBuyerReturnReliefRequest(
            TcsBuyerReturnReliefRegime.IncomeTaxAct1961FromApril2025,
            TcsBuyerReturnReliefCollectionCategory.Section206C1CLeaseOrNewActTable9,
            new DateOnly(2025, 4, 1), new DateOnly(2025, 7, 31),
            true, true, true, false));

        Assert.Equal(TcsBuyerReturnReliefStatus.EligibleForRelief, result.Status);
    }

    [Fact]
    public void Assess_NewActEligibleTableCategory_IsCovered()
    {
        var result = _service.Assess(new TcsBuyerReturnReliefRequest(
            TcsBuyerReturnReliefRegime.IncomeTaxAct2025,
            TcsBuyerReturnReliefCollectionCategory.Section206C1SpecifiedGoodsOrNewActTable1To5,
            new DateOnly(2026, 4, 1), new DateOnly(2026, 7, 31),
            true, true, true, false));

        Assert.Equal(TcsBuyerReturnReliefStatus.EligibleForRelief, result.Status);
        Assert.Contains("398(3)(c)", result.SectionReference);
    }

    [Fact]
    public void Assess_OtherCollectionProvision_IsNotEligible()
    {
        var result = _service.Assess(new TcsBuyerReturnReliefRequest(
            TcsBuyerReturnReliefRegime.IncomeTaxAct2025,
            TcsBuyerReturnReliefCollectionCategory.Other,
            new DateOnly(2026, 4, 1), new DateOnly(2026, 7, 31),
            true, true, true, false));

        Assert.Equal(TcsBuyerReturnReliefStatus.NotEligible, result.Status);
        Assert.False(result.CollectorNotDeemedInDefault);
        Assert.Null(result.InterestEndDate);
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void Assess_MissingStatutoryCondition_IsNotEligible(
        bool amountIncluded, bool taxPaid, bool certificate, bool returnDatePresent)
    {
        var result = _service.Assess(new TcsBuyerReturnReliefRequest(
            TcsBuyerReturnReliefRegime.IncomeTaxAct2025,
            TcsBuyerReturnReliefCollectionCategory.Section206C1CLeaseOrNewActTable9,
            new DateOnly(2026, 4, 1),
            returnDatePresent ? new DateOnly(2026, 7, 31) : null,
            amountIncluded, taxPaid, certificate, false));

        Assert.Equal(TcsBuyerReturnReliefStatus.NotEligible, result.Status);
        Assert.False(result.CollectorNotDeemedInDefault);
    }

    [Fact]
    public void Assess_AssessingOfficerOrderRequiresManualReview()
    {
        var result = _service.Assess(new TcsBuyerReturnReliefRequest(
            TcsBuyerReturnReliefRegime.IncomeTaxAct2025,
            TcsBuyerReturnReliefCollectionCategory.Section206C1SpecifiedGoodsOrNewActTable1To5,
            new DateOnly(2026, 4, 1), new DateOnly(2026, 7, 31),
            true, true, true, true));

        Assert.Equal(TcsBuyerReturnReliefStatus.ReviewRequired, result.Status);
        Assert.Null(result.InterestEndDate);
    }

    [Fact]
    public void Assess_RejectsPreAmendmentOldActDate()
    {
        var request = new TcsBuyerReturnReliefRequest(
            TcsBuyerReturnReliefRegime.IncomeTaxAct1961FromApril2025,
            TcsBuyerReturnReliefCollectionCategory.Section206C1SpecifiedGoodsOrNewActTable1To5,
            new DateOnly(2025, 3, 31), new DateOnly(2025, 7, 31),
            true, true, true, false);

        Assert.Throws<ArgumentException>(() => _service.Assess(request));
    }

    [Fact]
    public void Assess_RejectsBuyerReturnBeforeCollectibleDate()
    {
        var request = new TcsBuyerReturnReliefRequest(
            TcsBuyerReturnReliefRegime.IncomeTaxAct2025,
            TcsBuyerReturnReliefCollectionCategory.Section206C1SpecifiedGoodsOrNewActTable1To5,
            new DateOnly(2026, 4, 2), new DateOnly(2026, 4, 1),
            true, true, true, false);

        Assert.Throws<ArgumentException>(() => _service.Assess(request));
    }
}
