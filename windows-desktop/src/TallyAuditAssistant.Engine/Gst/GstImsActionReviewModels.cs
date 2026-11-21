namespace TallyAuditAssistant.Engine.Gst;

public enum GstImsSupplierAction { Accepted, Rejected, Pending, NoAction, DeemedAccepted, Unknown }
public enum GstImsReviewStatus { Accepted, Rejected, Pending, NoAction, DeemedAccepted, UnknownAction, DuplicateReference, DuplicateBooksReference, BooksOnly, PortalOnly }

/// <summary>One invoice/action row exported from the GST Invoice Management System.</summary>
public sealed record GstImsInvoiceAction(
    string SupplierGstin, string InvoiceNumber, DateTime? InvoiceDate, decimal InvoiceValue,
    GstImsSupplierAction Action, string? DocumentType = null, string? ReferenceId = null);

/// <summary>Purchase-book invoice supplied by the caller; no portal or books data is modified.</summary>
public sealed record GstImsBooksInvoice(
    string SupplierGstin, string InvoiceNumber, DateTime? InvoiceDate, decimal InvoiceValue,
    string? VoucherNumber = null, string? DocumentType = null);

public sealed record GstImsReviewItem(
    GstImsReviewStatus Status, string SupplierGstin, string InvoiceNumber,
    DateTime? InvoiceDate, decimal? PortalValue, decimal? BooksValue,
    string? VoucherNumber, decimal? ValueDifference, string Explanation,
    bool HasInvoiceDetailMismatch = false);

public sealed record GstImsValidationIssue(int SourceRow, string Code, string Message);

public sealed class GstImsActionReviewReport
{
    public int SourceRows { get; init; }
    public int ValidRows { get; init; }
    public IReadOnlyList<GstImsReviewItem> Items { get; init; } = Array.Empty<GstImsReviewItem>();
    public IReadOnlyList<GstImsValidationIssue> Issues { get; init; } = Array.Empty<GstImsValidationIssue>();
    public int RejectedCount => Items.Count(x => x.Status == GstImsReviewStatus.Rejected);
    public int PendingCount => Items.Count(x => x.Status == GstImsReviewStatus.Pending);
    public int DuplicateCount => Items.Count(x => x.Status == GstImsReviewStatus.DuplicateReference);
    public int DuplicateBooksReferenceCount => Items.Count(x => x.Status == GstImsReviewStatus.DuplicateBooksReference);
    public int DuplicateSourceReferenceCount => Issues.Count(x => x.Code == "DUPLICATE_SOURCE_REFERENCE_ID");
    public bool IsReadyForReview => Issues.Count == 0 && Items.All(x => x.Status is not GstImsReviewStatus.UnknownAction and not GstImsReviewStatus.DuplicateReference and not GstImsReviewStatus.DuplicateBooksReference && !x.HasInvoiceDetailMismatch);
}
