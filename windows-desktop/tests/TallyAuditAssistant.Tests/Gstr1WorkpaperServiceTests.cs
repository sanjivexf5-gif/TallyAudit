using TallyAuditAssistant.Engine.Gst;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class Gstr1WorkpaperServiceTests
{
    private readonly Gstr1WorkpaperService _service = new();
    private static readonly DateTime From = new(2026, 4, 1);
    private static readonly DateTime To = new(2026, 4, 30);

    [Fact]
    public void Prepare_GroupsOutwardSuppliesByCategoryPosAndRate_AndCalculatesTotals()
    {
        var rows = new[]
        {
            new Gstr1OutwardSupply("V1", "INV-1", new(2026,4,2), "27AAACB2222D1Z9", "27", Gstr1SupplyCategory.B2B, 18, 10000, 0, 900, 900, 0),
            new Gstr1OutwardSupply("V2", "INV-2", new(2026,4,3), "27AAACB2222D1Z9", "27", Gstr1SupplyCategory.B2B, 18, 5000, 0, 450, 450, 0),
            new Gstr1OutwardSupply("V3", "INV-3", new(2026,4,4), null, "29", Gstr1SupplyCategory.B2CS, 5, 2000, 100, 0, 0, 0)
        };
        var result = _service.Prepare(From, To, rows);
        Assert.Equal(3, result.IncludedRowCount);
        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Summaries.Count);
        var b2b = Assert.Single(result.Summaries.Where(x => x.Category == Gstr1SupplyCategory.B2B));
        Assert.Equal(2, b2b.DocumentCount); Assert.Equal(15000m, b2b.TaxableValue);
        Assert.Equal(1350m, b2b.Cgst); Assert.Equal(1350m, b2b.Sgst);
        Assert.Equal(2800m, result.TotalTax); // 2,700 B2B + 100 B2CS
        Assert.Equal(17000m, result.TaxableValue);
    }

    [Fact]
    public void Prepare_ExcludesInvalidRowsAndReportsEveryIssue()
    {
        var rows = new[]
        {
            new Gstr1OutwardSupply("V1", "DUP-1", new(2026,4,2), null, "27", Gstr1SupplyCategory.B2CS, 18, 1000, 180, 0, 0, 0),
            new Gstr1OutwardSupply("V2", "DUP-1", new(2026,4,2), null, "27", Gstr1SupplyCategory.B2CS, 18, 1000, 180, 0, 0, 0),
            new Gstr1OutwardSupply("V3", "BAD-TAX", new(2026,4,3), null, "27", Gstr1SupplyCategory.B2CS, 18, 1000, 0, 90, 90, 0),
            new Gstr1OutwardSupply("V4", "OUTSIDE", new(2026,5,1), null, "27", Gstr1SupplyCategory.B2CS, 18, 1000, 180, 0, 0, 0),
            new Gstr1OutwardSupply("V5", "NOTE-1", new(2026,4,5), null, "27", Gstr1SupplyCategory.B2CS, 18, 1000, 180, 0, 0, 0, true)
        };
        var result = _service.Prepare(From, To, rows);
        Assert.Equal(1, result.IncludedRowCount);
        Assert.Equal(4, result.Issues.Count);
        Assert.Contains(result.Issues, x => x.Code == "DUPLICATE_DOCUMENT");
        Assert.Contains(result.Issues, x => x.Code == "TAX_AMOUNT_MISMATCH");
        Assert.Contains(result.Issues, x => x.Code == "OUTSIDE_PERIOD");
        Assert.Contains(result.Issues, x => x.Code == "CREDIT_NOTE_REVIEW");
        Assert.False(result.IsReadyForReview);
    }

    [Fact]
    public void Prepare_RequiresRecipientGstinForB2B()
    {
        var row = new Gstr1OutwardSupply("V1", "INV-1", new(2026,4,2), null, "27", Gstr1SupplyCategory.B2B, 18, 1000, 0, 90, 90, 0);
        var result = _service.Prepare(From, To, new[] { row });
        Assert.Empty(result.Summaries);
        Assert.Contains(result.Issues, x => x.Code == "INVALID_RECIPIENT_GSTIN");
    }

    [Fact]
    public void Prepare_RejectsInvalidPeriod()
    {
        Assert.Throws<ArgumentException>(() => _service.Prepare(To, From, Array.Empty<Gstr1OutwardSupply>()));
    }
}
