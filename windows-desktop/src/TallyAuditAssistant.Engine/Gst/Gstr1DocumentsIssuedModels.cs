namespace TallyAuditAssistant.Engine.Gst;

public enum Gstr1DocumentType
{
    OutwardSupplyInvoice,
    InwardSupplyInvoiceFromUnregistered,
    RevisedInvoice,
    DebitNote,
    CreditNote,
    ReceiptVoucher,
    PaymentVoucher,
    RefundVoucher,
    DeliveryChallanJobWork,
    DeliveryChallanApproval,
    DeliveryChallanLiquidGas,
    DeliveryChallanOther
}

/// <summary>One document-number series reported in GSTR-1 Table 13.</summary>
public sealed record Gstr1DocumentSeries(
    Gstr1DocumentType DocumentType,
    string SerialNumberFrom,
    string SerialNumberTo,
    int TotalDocuments,
    int CancelledDocuments,
    string? EvidenceReference = null)
{
    public int NetIssuedDocuments => TotalDocuments - CancelledDocuments;
}

public sealed record Gstr1DocumentsIssuedIssue(
    Gstr1DocumentType? DocumentType,
    string? SerialNumberFrom,
    string? SerialNumberTo,
    string Code,
    string Message);

/// <summary>Indicates whether the listed GSTR-1 tables contain reportable data for this period.</summary>
public sealed record Gstr1Table13Applicability(
    bool HasTable4Data = false,
    bool HasTable5Data = false,
    bool HasTable6Data = false,
    bool HasTable7Data = false,
    bool HasTable8Data = false,
    bool HasTable9Data = false,
    bool HasTable10Data = false,
    bool HasTable14Data = false,
    bool HasTable15Data = false)
{
    public bool RequiresDocumentsIssued => HasTable4Data || HasTable5Data || HasTable6Data ||
        HasTable7Data || HasTable8Data || HasTable9Data || HasTable10Data || HasTable14Data || HasTable15Data;
}

public sealed class Gstr1DocumentsIssuedWorkpaper
{
    public DateTime ReturnPeriodStart { get; init; }
    public DateTime ReturnPeriodEnd { get; init; }
    public int SourceSeriesCount { get; init; }
    public int IncludedSeriesCount { get; init; }
    public int TotalDocuments { get; init; }
    public int CancelledDocuments { get; init; }
    public int NetIssuedDocuments { get; init; }
    public IReadOnlyList<Gstr1DocumentSeries> Series { get; init; } = Array.Empty<Gstr1DocumentSeries>();
    public IReadOnlyList<Gstr1DocumentsIssuedIssue> Issues { get; init; } = Array.Empty<Gstr1DocumentsIssuedIssue>();
    public bool IsTableRequired { get; init; }
    public bool IsReadyForReview => SourceSeriesCount == IncludedSeriesCount && Issues.Count == 0;
}
