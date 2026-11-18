using TallyAuditAssistant.Engine.Gst;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class Gstr1Gstr3BReconciliationServiceTests
{
    [Fact]
    public void Reconcile_ComparesNetGstr1AgainstOutwardAndZeroRatedBuckets_ExcludesRcm()
    {
        var gstr1 = new Gstr1Workpaper
        {
            FromDate = new(2026,4,1), ToDate = new(2026,4,30),
            Summaries = new[] { new Gstr1TaxSummary(Gstr1SupplyCategory.B2B, "27", 18, 1, 10000, 0, 900, 900, 0) },
            NoteSummaries = new[] { new Gstr1NoteSummary(Gstr1AdjustmentNoteType.CreditNote, Gstr1SupplyCategory.B2B,
                "27", 18, 1, 1000, 0, 90, 90, 0) }
        };
        var gstr3B = new Gstr3BWorkpaper
        {
            Liabilities = new[]
            {
                new Gstr3BLiabilitySummary(Gstr3BLiabilitySection.OutwardTaxable, 1, 9000, new(0,810,810,0)),
                new Gstr3BLiabilitySummary(Gstr3BLiabilitySection.ReverseCharge, 1, 2000, new(360,0,0,0))
            }
        };
        var result = new Gstr1Gstr3BReconciliationService().Reconcile(gstr1, gstr3B);
        Assert.False(result.HasDifferences);
        Assert.All(result.Variances, x => Assert.Equal(0m, x.Difference));
    }

    [Fact]
    public void Reconcile_ReportsPerTaxHeadVarianceAndHonoursTolerance()
    {
        var gstr1 = new Gstr1Workpaper
        {
            Summaries = new[] { new Gstr1TaxSummary(Gstr1SupplyCategory.B2B, "27", 18, 1, 10000, 0, 900, 900, 0) }
        };
        var gstr3B = new Gstr3BWorkpaper
        {
            Liabilities = new[] { new Gstr3BLiabilitySummary(Gstr3BLiabilitySection.OutwardTaxable, 1, 10000, new(0,890,900,0)) }
        };
        var result = new Gstr1Gstr3BReconciliationService().Reconcile(gstr1, gstr3B, 1m);
        Assert.True(result.HasDifferences);
        Assert.Contains(result.Variances, x => x.TaxHead == "CGST" && x.Difference == 10m && x.ExceedsTolerance);
    }

    [Fact]
    public void Reconcile_RejectsNegativeTolerance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Gstr1Gstr3BReconciliationService()
            .Reconcile(new Gstr1Workpaper(), new Gstr3BWorkpaper(), -1m));
    }
}
