using TallyAuditAssistant.Engine.Gst;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class Gstr1DocumentsIssuedWorkpaperServiceTests
{
    private readonly Gstr1DocumentsIssuedWorkpaperService _service = new();

    [Fact]
    public void Prepare_TotalsDocumentSeriesAndCalculatesNetIssued()
    {
        var rows = new[]
        {
            new Gstr1DocumentSeries(Gstr1DocumentType.OutwardSupplyInvoice, "INV-001", "INV-100", 95, 5),
            new Gstr1DocumentSeries(Gstr1DocumentType.CreditNote, "CN-001", "CN-010", 8, 1),
            new Gstr1DocumentSeries(Gstr1DocumentType.ReceiptVoucher, "RV-01", "RV-05", 4, 0)
        };

        var result = _service.Prepare(new(2026, 4, 1), new(2026, 4, 30), rows);

        Assert.True(result.IsReadyForReview);
        Assert.Equal(107, result.TotalDocuments);
        Assert.Equal(6, result.CancelledDocuments);
        Assert.Equal(101, result.NetIssuedDocuments);
        Assert.Equal(3, result.IncludedSeriesCount);
        Assert.Equal(101, result.Series.Sum(x => x.NetIssuedDocuments));
    }

    [Fact]
    public void Prepare_RejectsInvalidCountsAndReversedOrOversizedRanges()
    {
        var rows = new[]
        {
            new Gstr1DocumentSeries(Gstr1DocumentType.OutwardSupplyInvoice, "INV-001", "INV-010", 10, 11),
            new Gstr1DocumentSeries(Gstr1DocumentType.CreditNote, "CN-010", "CN-001", 5, 0),
            new Gstr1DocumentSeries(Gstr1DocumentType.DebitNote, "DN-001", "DN-003", 4, 0),
            new Gstr1DocumentSeries(Gstr1DocumentType.ReceiptVoucher, "RV-001", "RV-001", 2, 0)
        };

        var result = _service.Prepare(new(2026, 4, 1), new(2026, 4, 30), rows);

        Assert.Empty(result.Series);
        Assert.Contains(result.Issues, x => x.Code == "INVALID_CANCELLED_COUNT");
        Assert.Contains(result.Issues, x => x.Code == "REVERSED_SERIAL_RANGE");
        Assert.Contains(result.Issues, x => x.Code == "TOTAL_EXCEEDS_SERIAL_RANGE");
    }

    [Fact]
    public void Prepare_RejectsDuplicateAndOverlappingRangesWithinSameDocumentCategory()
    {
        var rows = new[]
        {
            new Gstr1DocumentSeries(Gstr1DocumentType.OutwardSupplyInvoice, "INV-001", "INV-050", 50, 0),
            new Gstr1DocumentSeries(Gstr1DocumentType.OutwardSupplyInvoice, "INV-040", "INV-060", 21, 0),
            new Gstr1DocumentSeries(Gstr1DocumentType.OutwardSupplyInvoice, "INV-001", "INV-050", 50, 0),
            new Gstr1DocumentSeries(Gstr1DocumentType.CreditNote, "INV-001", "INV-050", 50, 0)
        };

        var result = _service.Prepare(new(2026, 4, 1), new(2026, 4, 30), rows);

        Assert.Equal(2, result.IncludedSeriesCount);
        Assert.Contains(result.Issues, x => x.Code == "OVERLAPPING_SERIAL_RANGE");
        Assert.Contains(result.Issues, x => x.Code == "DUPLICATE_DOCUMENT_SERIES");
    }

    [Fact]
    public void Prepare_RequiresTable13FromMay2025WhenSpecifiedTablesContainData()
    {
        var applicability = new Gstr1Table13Applicability(HasTable4Data: true);
        var result = _service.Prepare(new(2025, 5, 1), new(2025, 5, 31),
            Array.Empty<Gstr1DocumentSeries>(), applicability);

        Assert.True(result.IsTableRequired);
        Assert.Contains(result.Issues, x => x.Code == "TABLE13_REQUIRED");
        Assert.False(result.IsReadyForReview);
    }

    [Fact]
    public void Prepare_DoesNotRequireTable13BeforeMay2025OrWithoutApplicableData()
    {
        var beforeStart = _service.Prepare(new(2025, 4, 1), new(2025, 4, 30),
            Array.Empty<Gstr1DocumentSeries>(), new Gstr1Table13Applicability(HasTable4Data: true));
        var noApplicableData = _service.Prepare(new(2025, 5, 1), new(2025, 5, 31),
            Array.Empty<Gstr1DocumentSeries>(), new Gstr1Table13Applicability());

        Assert.False(beforeStart.IsTableRequired);
        Assert.Empty(beforeStart.Issues);
        Assert.False(noApplicableData.IsTableRequired);
        Assert.Empty(noApplicableData.Issues);
    }

    [Fact]
    public void Prepare_DoesNotTreatSameSerialRangeInDifferentDocumentCategoriesAsDuplicate()
    {
        var rows = new[]
        {
            new Gstr1DocumentSeries(Gstr1DocumentType.OutwardSupplyInvoice, "001", "010", 10, 0),
            new Gstr1DocumentSeries(Gstr1DocumentType.DebitNote, "001", "010", 10, 0)
        };
        var result = _service.Prepare(new(2026, 4, 1), new(2026, 4, 30), rows);
        Assert.True(result.IsReadyForReview);
        Assert.Equal(2, result.IncludedSeriesCount);
    }

    [Fact]
    public void Prepare_RejectsInvalidPeriod()
    {
        Assert.Throws<ArgumentException>(() => _service.Prepare(new(2026, 5, 1), new(2026, 4, 1),
            Array.Empty<Gstr1DocumentSeries>()));
    }
}
