using TallyAuditAssistant.Engine.Gst;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class Gstr3BWorkpaperServiceTests
{
    private readonly Gstr3BWorkpaperService _service = new();
    private static readonly DateTime From = new(2026, 4, 1);
    private static readonly DateTime To = new(2026, 4, 30);

    [Fact]
    public void Prepare_AggregatesLiabilityItcAndReversalsByTaxHead()
    {
        var liabilities = new[]
        {
            new Gstr3BLiabilityLine("S1", new(2026,4,2), Gstr3BLiabilitySection.OutwardTaxable, 10000, 0, 900, 900, 0),
            new Gstr3BLiabilityLine("S2", new(2026,4,3), Gstr3BLiabilitySection.ReverseCharge, 2000, 360, 0, 0, 0),
            new Gstr3BLiabilityLine("S3", new(2026,4,4), Gstr3BLiabilitySection.ZeroRated, 5000, 0, 0, 0, 0)
        };
        var itc = new[]
        {
            new Gstr3BItcLine("I1", new(2026,4,5), Gstr3BItcCategory.ImportGoods, 100, 200, 200, 0),
            new Gstr3BItcLine("I2", new(2026,4,6), Gstr3BItcCategory.OtherEligible, 0, 300, 300, 10)
        };
        var reversals = new[]
        {
            new Gstr3BItcReversal("R1", new(2026,4,7), Gstr3BReversalCategory.OtherReversal, 0, 50, 50, 0)
        };

        var result = _service.Prepare(From, To, liabilities, itc, reversals);
        Assert.Equal(3, result.Liabilities.Count);
        Assert.Equal(2000m, result.TotalTaxLiability.Total);
        Assert.Equal(100m, result.TotalItcAvailable.Igst);
        Assert.Equal(500m, result.TotalItcAvailable.Cgst);
        Assert.Equal(450m, result.NetItcAfterReversals.Cgst);
        Assert.Equal(450m, result.NetItcAfterReversals.Sgst);
        Assert.Equal(10m, result.NetItcAfterReversals.Cess);
        Assert.Empty(result.Issues);
        Assert.True(result.IsReadyForReview);
    }

    [Fact]
    public void Prepare_ExcludesOutOfPeriodNegativeAndDuplicateSources()
    {
        var liabilities = new[]
        {
            new Gstr3BLiabilityLine("DUP", new(2026,4,2), Gstr3BLiabilitySection.OutwardTaxable, 1000, 0, 90, 90, 0),
            new Gstr3BLiabilityLine("DUP", new(2026,4,2), Gstr3BLiabilitySection.OutwardTaxable, 1000, 0, 90, 90, 0),
            new Gstr3BLiabilityLine("OLD", new(2026,3,31), Gstr3BLiabilitySection.OutwardTaxable, 1000, 0, 90, 90, 0),
            new Gstr3BLiabilityLine("NEG", new(2026,4,4), Gstr3BLiabilitySection.OutwardTaxable, -100, 0, 0, 0, 0)
        };
        var result = _service.Prepare(From, To, liabilities, Array.Empty<Gstr3BItcLine>(), Array.Empty<Gstr3BItcReversal>());
        Assert.Single(result.Liabilities);
        Assert.Contains(result.Issues, x => x.Code == "DUPLICATE_SOURCE_ID");
        Assert.Contains(result.Issues, x => x.Code == "OUTSIDE_PERIOD");
        Assert.Contains(result.Issues, x => x.Code == "NEGATIVE_AMOUNT");
        Assert.False(result.IsReadyForReview);
    }

    [Fact]
    public void Prepare_FlagsReversalGreaterThanAvailableCredit()
    {
        var itc = new[] { new Gstr3BItcLine("I1", new(2026,4,5), Gstr3BItcCategory.OtherEligible, 0, 100, 100, 0) };
        var reversals = new[] { new Gstr3BItcReversal("R1", new(2026,4,6), Gstr3BReversalCategory.OtherReversal, 0, 150, 100, 0) };
        var result = _service.Prepare(From, To, Array.Empty<Gstr3BLiabilityLine>(), itc, reversals);
        Assert.Contains(result.Issues, x => x.Code == "REVERSAL_EXCEEDS_ITC");
        Assert.Equal(-50m, result.NetItcAfterReversals.Cgst);
        Assert.False(result.IsReadyForReview);
    }

    [Fact]
    public void Prepare_RejectsInvalidPeriodAndNullInputs()
    {
        Assert.Throws<ArgumentException>(() => _service.Prepare(To, From, Array.Empty<Gstr3BLiabilityLine>(),
            Array.Empty<Gstr3BItcLine>(), Array.Empty<Gstr3BItcReversal>()));
        Assert.Throws<ArgumentNullException>(() => _service.Prepare(From, To, null!,
            Array.Empty<Gstr3BItcLine>(), Array.Empty<Gstr3BItcReversal>()));
    }
}
