using TallyAuditAssistant.Engine.Gst;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class GstImsActionReviewServiceTests
{
    private const string Gstin = "27AAPFU0939F1ZV";

    [Fact]
    public void Review_ClassifiesActionsAndReconcilesPurchaseBookValues()
    {
        var portal = new[]
        {
            new GstImsInvoiceAction(Gstin, "INV-1", new(2026, 5, 1), 118000m, GstImsSupplierAction.Accepted),
            new GstImsInvoiceAction(Gstin, "INV-2", new(2026, 5, 2), 59000m, GstImsSupplierAction.Rejected),
            new GstImsInvoiceAction(Gstin, "INV-3", new(2026, 5, 3), 23600m, GstImsSupplierAction.Pending),
            new GstImsInvoiceAction(Gstin, "INV-4", new(2026, 5, 4), 11800m, GstImsSupplierAction.NoAction),
            new GstImsInvoiceAction(Gstin, "INV-5", new(2026, 5, 5), 100m, GstImsSupplierAction.DeemedAccepted)
        };
        var books = new[]
        {
            new GstImsBooksInvoice(Gstin, "INV 1", new(2026, 5, 1), 118000m, "PUR-1"),
            new GstImsBooksInvoice(Gstin, "INV-2", new(2026, 5, 2), 60000m, "PUR-2"),
            new GstImsBooksInvoice(Gstin, "INV-3", new(2026, 5, 3), 23600m, "PUR-3"),
            new GstImsBooksInvoice(Gstin, "INV-6", new(2026, 5, 6), 500m, "PUR-6")
        };

        var report = new GstImsActionReviewService().Review(portal, books);

        Assert.Equal(5, report.ValidRows);
        Assert.Single(report.Items, x => x.Status == GstImsReviewStatus.Accepted);
        Assert.Single(report.Items, x => x.Status == GstImsReviewStatus.Rejected);
        Assert.Single(report.Items, x => x.Status == GstImsReviewStatus.Pending);
        Assert.Single(report.Items, x => x.Status == GstImsReviewStatus.NoAction);
        Assert.Single(report.Items, x => x.Status == GstImsReviewStatus.DeemedAccepted);
        Assert.Single(report.Items, x => x.Status == GstImsReviewStatus.BooksOnly);
        Assert.Equal(1000m, Assert.Single(report.Items, x => x.InvoiceNumber == "INV-2").ValueDifference);
    }

    [Fact]
    public void Review_RejectsInvalidRowsAndFlagsDuplicateKeysWithoutGuessing()
    {
        var portal = new[]
        {
            new GstImsInvoiceAction("BAD", "INV-1", null, 100m, GstImsSupplierAction.Accepted),
            new GstImsInvoiceAction(Gstin, "", null, 100m, GstImsSupplierAction.Accepted),
            new GstImsInvoiceAction(Gstin, "INV-2", null, 100m, GstImsSupplierAction.Accepted),
            new GstImsInvoiceAction(Gstin, "INV 2", null, 100m, GstImsSupplierAction.Rejected)
        };

        var report = new GstImsActionReviewService().Review(portal, Array.Empty<GstImsBooksInvoice>());

        Assert.Equal(2, report.ValidRows);
        Assert.Equal(2, report.Issues.Count(x => x.Code == "DUPLICATE_INVOICE_REFERENCE"));
        Assert.Single(report.Issues, x => x.Code == "INVALID_SUPPLIER_GSTIN");
        Assert.Single(report.Issues, x => x.Code == "MISSING_INVOICE_NUMBER");
        Assert.Equal(2, report.Items.Count(x => x.Status == GstImsReviewStatus.DuplicateReference));
        Assert.False(report.IsReadyForReview);
    }

    [Fact]
    public void Review_FlagsRepeatedNonEmptySourceReferenceIdsAndRequiresReview()
    {
        var portal = new[]
        {
            new GstImsInvoiceAction(Gstin, "INV-21", null, 100m,
                GstImsSupplierAction.Accepted, ReferenceId: " REF-001 "),
            new GstImsInvoiceAction(Gstin, "INV-22", null, 200m,
                GstImsSupplierAction.Pending, ReferenceId: "ref-001"),
            new GstImsInvoiceAction(Gstin, "INV-23", null, 300m,
                GstImsSupplierAction.Accepted, ReferenceId: " ")
        };

        var report = new GstImsActionReviewService().Review(portal, Array.Empty<GstImsBooksInvoice>());

        Assert.Equal(2, report.DuplicateSourceReferenceCount);
        Assert.Equal(2, report.Issues.Count(x => x.Code == "DUPLICATE_SOURCE_REFERENCE_ID"));
        Assert.Equal(3, report.ValidRows);
        Assert.False(report.IsReadyForReview);
    }

    [Fact]
    public void Review_FlagsDuplicatePurchaseBookReferencesAndDoesNotGuessAMatch()
    {
        var portal = new[]
        {
            new GstImsInvoiceAction(Gstin, "INV-9", new(2026, 5, 9), 100m, GstImsSupplierAction.Accepted)
        };
        var books = new[]
        {
            new GstImsBooksInvoice(Gstin, "INV 9", new(2026, 5, 9), 100m, "PUR-9A"),
            new GstImsBooksInvoice(Gstin, "INV-9", new(2026, 5, 10), 100m, "PUR-9B")
        };

        var report = new GstImsActionReviewService().Review(portal, books);

        Assert.Single(report.Items, x => x.Status == GstImsReviewStatus.PortalOnly);
        Assert.Equal(2, report.DuplicateBooksReferenceCount);
        Assert.Equal(2, report.Items.Count(x => x.Status == GstImsReviewStatus.DuplicateBooksReference));
        Assert.False(report.IsReadyForReview);
    }

    [Fact]
    public void Review_ReportsInvalidPurchaseBookRowsInsteadOfSilentlyDroppingThem()
    {
        var books = new GstImsBooksInvoice[]
        {
            new("BAD", "INV-1", null, 100m),
            new(Gstin, "", null, 100m),
            new(Gstin, "INV-3", null, -1m),
            null!
        };

        var report = new GstImsActionReviewService().Review(
            Array.Empty<GstImsInvoiceAction>(), books);

        Assert.Contains(report.Issues, x => x.Code == "BOOKS_INVALID_SUPPLIER_GSTIN");
        Assert.Contains(report.Issues, x => x.Code == "BOOKS_MISSING_INVOICE_NUMBER");
        Assert.Contains(report.Issues, x => x.Code == "BOOKS_NEGATIVE_INVOICE_VALUE");
        Assert.Contains(report.Issues, x => x.Code == "BOOKS_NULL_ROW");
        Assert.Empty(report.Items);
        Assert.False(report.IsReadyForReview);
    }

    [Fact]
    public void Review_DoesNotTreatAcceptedActionAsProofOfItcEligibility()
    {
        var report = new GstImsActionReviewService().Review(
            new[] { new GstImsInvoiceAction(Gstin, "INV-1", null, 100m, GstImsSupplierAction.Accepted) },
            Array.Empty<GstImsBooksInvoice>());

        Assert.Contains("does not establish ITC eligibility", Assert.Single(report.Items).Explanation);
    }

    [Theory]
    [InlineData("27AAPFU0939F1ZV", true)]
    [InlineData("27AAPFU0939F1ZA", false)]
    [InlineData("27AAPFUO939F1ZV", false)]
    [InlineData("BAD", false)]
    public void Review_ValidatesGstinStructureAndChecksum(string gstin, bool expectedValid)
    {
        var report = new GstImsActionReviewService().Review(
            new[] { new GstImsInvoiceAction(gstin, "INV-1", null, 100m, GstImsSupplierAction.Accepted) },
            Array.Empty<GstImsBooksInvoice>());

        Assert.Equal(expectedValid ? 1 : 0, report.ValidRows);
        Assert.Equal(expectedValid, !report.Issues.Any(x => x.Code == "INVALID_SUPPLIER_GSTIN"));
    }

    [Fact]
    public void Review_FlagsInvoiceDateAndDocumentTypeMismatchWithoutHidingSupplierAction()
    {
        var portal = new[]
        {
            new GstImsInvoiceAction(Gstin, "INV-10", new(2026, 5, 10), 118000m,
                GstImsSupplierAction.Accepted, DocumentType: "Invoice")
        };
        var books = new[]
        {
            new GstImsBooksInvoice(Gstin, "INV-10", new(2026, 5, 11), 118000m,
                "PUR-10", "Credit Note")
        };

        var report = new GstImsActionReviewService().Review(portal, books);
        var item = Assert.Single(report.Items);

        Assert.Equal(GstImsReviewStatus.Accepted, item.Status);
        Assert.True(item.HasInvoiceDetailMismatch);
        Assert.Contains("Invoice date differs", item.Explanation);
        Assert.Contains("Document type differs", item.Explanation);
        Assert.False(report.IsReadyForReview);
    }

    [Fact]
    public void Review_DoesNotFlagInvoiceDetailsWhenSourceFieldsAreUnavailable()
    {
        var portal = new[]
        {
            new GstImsInvoiceAction(Gstin, "INV-11", null, 100m, GstImsSupplierAction.Accepted)
        };
        var books = new[]
        {
            new GstImsBooksInvoice(Gstin, "INV-11", null, 100m)
        };

        var report = new GstImsActionReviewService().Review(portal, books);

        Assert.False(Assert.Single(report.Items).HasInvoiceDetailMismatch);
        Assert.True(report.IsReadyForReview);
    }

    [Fact]
    public void ReviewCsv_ParsesSnapshotAndRunsFullReviewPipeline()
    {
        const string csv = "Supplier GSTIN,Invoice Number,Invoice Date,Invoice Value,IMS Action,Document Type,Reference ID\\n" +
            "27AAPFU0939F1ZV,INV-CSV-1,10-05-2026,118000,Accepted,Invoice,REF-CSV-1";
        var books = new[]
        {
            new GstImsBooksInvoice(Gstin, "INV CSV 1", new(2026, 5, 10), 118000m, "PUR-CSV-1", "Invoice")
        };

        var report = new GstImsActionReviewService().ReviewCsv(csv, books);

        Assert.Equal(1, report.SourceRows);
        Assert.Equal(1, report.ValidRows);
        Assert.Empty(report.Issues);
        var item = Assert.Single(report.Items);
        Assert.Equal(GstImsReviewStatus.Accepted, item.Status);
        Assert.Equal("PUR-CSV-1", item.VoucherNumber);
        Assert.True(report.IsReadyForReview);
    }

    [Fact]
    public void ReviewCsv_RejectsMalformedSnapshotInsteadOfReturningPartialReview()
    {
        const string csv = "GSTIN,Invoice Number,Invoice Value\\n27AAPFU0939F1ZV,INV-1,100";

        Assert.Throws<InvalidDataException>(() =>
            new GstImsActionReviewService().ReviewCsv(csv, Array.Empty<GstImsBooksInvoice>()));
    }

    [Fact]
    public void Review_RejectsNegativeTolerance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GstImsActionReviewService().Review(
                Array.Empty<GstImsInvoiceAction>(), Array.Empty<GstImsBooksInvoice>(), -0.01m));
    }
}
