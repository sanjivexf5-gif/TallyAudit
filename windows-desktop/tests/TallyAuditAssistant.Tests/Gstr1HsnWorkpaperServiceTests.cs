using TallyAuditAssistant.Engine.Gst;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Gstr1HsnWorkpaperServiceTests
{
    private readonly Gstr1HsnWorkpaperService _service = new();

    [Fact]
    public void Prepare_GroupsByB2bB2cHsnUqcAndRate()
    {
        var rows = new[]
        {
            new Gstr1HsnLine("V1", Gstr1HsnSupplyTab.B2B, "9983", "Consultancy", "OTH", 2, 18, 10000, 0, 900, 900, 0),
            new Gstr1HsnLine("V2", Gstr1HsnSupplyTab.B2B, "9983", "Consultancy", "OTH", 3, 18, 5000, 0, 450, 450, 0),
            new Gstr1HsnLine("V3", Gstr1HsnSupplyTab.B2C, "9983", "Consultancy", "OTH", 1, 18, 1000, 180, 0, 0, 0)
        };
        var result = _service.Prepare(new(2026, 4, 1), new(2026, 4, 30), 4_00_00_000m, rows);

        Assert.True(result.IsReadyForReview);
        Assert.Equal(2, result.Summaries.Count);
        var b2b = Assert.Single(result.Summaries, x => x.SupplyTab == Gstr1HsnSupplyTab.B2B);
        Assert.Equal(2, b2b.SourceLineCount);
        Assert.Equal(5m, b2b.Quantity);
        Assert.Equal(15000m, b2b.TaxableValue);
        Assert.Equal(1350m, b2b.Cgst);
        Assert.Equal(1350m, b2b.Sgst);
        Assert.Equal(180m, Assert.Single(result.Summaries, x => x.SupplyTab == Gstr1HsnSupplyTab.B2C).Igst);
    }

    [Fact]
    public void Prepare_RequiresSixDigitHsnWhenPreviousYearTurnoverExceedsFiveCrore()
    {
        var row = new Gstr1HsnLine("V1", Gstr1HsnSupplyTab.B2B, "9983", "Consultancy", "OTH", 1, 18, 1000, 0, 90, 90, 0);
        var result = _service.Prepare(new(2026, 4, 1), new(2026, 4, 30), 5_00_00_001m, new[] { row });
        Assert.Empty(result.Summaries);
        Assert.Contains(result.Issues, x => x.Code == "INVALID_HSN_LENGTH");
        Assert.False(result.IsReadyForReview);
    }

    [Fact]
    public void Prepare_AcceptsFourDigitHsnAtOrBelowFiveCroreTurnover()
    {
        var row = new Gstr1HsnLine("V1", Gstr1HsnSupplyTab.B2C, "9983", "Consultancy", "OTH", 1, 18, 1000, 180, 0, 0, 0);
        var result = _service.Prepare(new(2026, 4, 1), new(2026, 4, 30), 5_00_00_000m, new[] { row });
        Assert.True(result.IsReadyForReview);
        Assert.Single(result.Summaries);
    }

    [Fact]
    public void Prepare_ReportsMissingDescriptionUqcAndTaxMismatch()
    {
        var rows = new[]
        {
            new Gstr1HsnLine("V1", Gstr1HsnSupplyTab.B2B, "9983", "", "OTH", 1, 18, 1000, 180, 0, 0, 0),
            new Gstr1HsnLine("V2", Gstr1HsnSupplyTab.B2B, "9983", "Consultancy", "", 1, 18, 1000, 180, 0, 0, 0),
            new Gstr1HsnLine("V3", Gstr1HsnSupplyTab.B2C, "9983", "Consultancy", "OTH", 1, 18, 1000, 100, 0, 0, 0)
        };
        var result = _service.Prepare(new(2026, 4, 1), new(2026, 4, 30), 1m, rows);
        Assert.Contains(result.Issues, x => x.Code == "MISSING_HSN_DESCRIPTION");
        Assert.Contains(result.Issues, x => x.Code == "MISSING_UQC");
        Assert.Contains(result.Issues, x => x.Code == "TAX_AMOUNT_MISMATCH");
        Assert.Equal(0, result.IncludedLineCount);
    }

    [Fact]
    public void Prepare_RejectsInvalidPeriodAndNegativeTurnover()
    {
        Assert.Throws<ArgumentException>(() => _service.Prepare(new(2026, 5, 1), new(2026, 4, 1), 0, Array.Empty<Gstr1HsnLine>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Prepare(new(2026, 4, 1), new(2026, 4, 30), -1, Array.Empty<Gstr1HsnLine>()));
    }
}
