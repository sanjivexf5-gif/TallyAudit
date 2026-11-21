using TallyAuditAssistant.Engine.Gst;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class GstImsActionReviewServiceTests
{
    private const string Gstin = "27AAACB2222D1Z9";

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
    public void Review_DoesNotTreatAcceptedActionAsProofOfItcEligibility()
    {
        var report = new GstImsActionReviewService().Review(
            new[] { new GstImsInvoiceAction(Gstin, "INV-1", null, 100m, GstImsSupplierAction.Accepted) },
            Array.Empty<GstImsBooksInvoice>());

        Assert.Contains("does not establish ITC eligibility", Assert.Single(report.Items).Explanation);
    }

    [Fact]
    public void Review_RejectsNegativeTolerance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GstImsActionReviewService().Review(
                Array.Empty<GstImsInvoiceAction>(), Array.Empty<GstImsBooksInvoice>(), -0.01m));
    }
}
