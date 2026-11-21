using System;
using TallyAuditAssistant.Engine.Tds.Returns;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class TcsBuyerReturnReliefAssessmentServiceTests
{
    private readonly TcsBuyerReturnReliefAssessmentService _service = new();

    [Fact]
    public void Assess_QualifyingOldActSection206C1_UsesBuyerReturnDateAndForm27BA()
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
        Assert.Equal("Form 27BA", result.AccountantCertificateForm);
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
    public void Assess_NewActEligibleTableCategory_UsesForm150()
    {
        var result = _service.Assess(new TcsBuyerReturnReliefRequest(
            TcsBuyerReturnReliefRegime.IncomeTaxAct2025,
            TcsBuyerReturnReliefCollectionCategory.Section206C1SpecifiedGoodsOrNewActTable1To5,
            new DateOnly(2026, 4, 1), new DateOnly(2026, 7, 31),
            true, true, true, false));

        Assert.Equal(TcsBuyerReturnReliefStatus.EligibleForRelief, result.Status);
        Assert.Equal("Form 150", result.AccountantCertificateForm);
        Assert.Contains("398(3)(c)", result.SectionReference);
        Assert.Contains("Rule 221", result.SourceReference);
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

    [Fact]
    public void Assess_MissingEvidenceRequiresReviewInsteadOfDeclaringReliefIneligible()
    {
        var result = _service.Assess(new TcsBuyerReturnReliefRequest(
            TcsBuyerReturnReliefRegime.IncomeTaxAct2025,
            TcsBuyerReturnReliefCollectionCategory.Section206C1CLeaseOrNewActTable9,
            new DateOnly(2026, 4, 1), null,
            null, null, null, false));

        Assert.Equal(TcsBuyerReturnReliefStatus.ReviewRequired, result.Status);
        Assert.False(result.CollectorNotDeemedInDefault);
        Assert.Null(result.InterestEndDate);
    }

    [Fact]
    public void Assess_ConfirmedUnpaidTaxDoesNotQualify()
    {
        var result = _service.Assess(new TcsBuyerReturnReliefRequest(
            TcsBuyerReturnReliefRegime.IncomeTaxAct2025,
            TcsBuyerReturnReliefCollectionCategory.Section206C1CLeaseOrNewActTable9,
            new DateOnly(2026, 4, 1), new DateOnly(2026, 7, 31),
            true, false, true, false));

        Assert.Equal(TcsBuyerReturnReliefStatus.NotEligible, result.Status);
    }

    [Fact]
    public void Assess_MissingCertificateRequiresReview()
    {
        var result = _service.Assess(new TcsBuyerReturnReliefRequest(
            TcsBuyerReturnReliefRegime.IncomeTaxAct2025,
            TcsBuyerReturnReliefCollectionCategory.Section206C1CLeaseOrNewActTable9,
            new DateOnly(2026, 4, 1), new DateOnly(2026, 7, 31),
            true, true, null, false));

        Assert.Equal(TcsBuyerReturnReliefStatus.ReviewRequired, result.Status);
        Assert.Equal("Form 150", result.AccountantCertificateForm);
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
    public void Assess_PreAmendmentOldActDate_IsCoveredBySection206C6AAndForm27BA()
    {
        var request = new TcsBuyerReturnReliefRequest(
            TcsBuyerReturnReliefRegime.IncomeTaxAct1961BeforeApril2025,
            TcsBuyerReturnReliefCollectionCategory.Section206C1SpecifiedGoodsOrNewActTable1To5,
            new DateOnly(2025, 3, 31), new DateOnly(2025, 7, 31),
            true, true, true, false);

        var result = _service.Assess(request);
        Assert.Equal(TcsBuyerReturnReliefStatus.EligibleForRelief, result.Status);
        Assert.Equal("Form 27BA", result.AccountantCertificateForm);
        Assert.Equal(new DateOnly(2025, 7, 31), result.InterestEndDate);
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
