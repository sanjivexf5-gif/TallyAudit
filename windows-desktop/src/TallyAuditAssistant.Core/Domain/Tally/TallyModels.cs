namespace TallyAuditAssistant.Core.Domain.Tally;

public enum TallyRequestFormat
{
    Xml,
    Json
}

public class TallyCompanyProfile
{
    public string Name { get; set; } = string.Empty;
    public string? FormalName { get; set; }
    public string? GSTIN { get; set; }
    public string? PAN { get; set; }
    public string? StateName { get; set; }
    public string? StateCode { get; set; }
    public DateTime BooksBeginningFrom { get; set; }
    public DateTime? FinancialYearFrom { get; set; }
    public string? BaseCurrencySymbol { get; set; }
    public long AlterId { get; set; }
}

public class TallyLedgerDto
{
    public string Name { get; set; } = string.Empty;
    public string ParentGroup { get; set; } = string.Empty;
    public string? GSTIN { get; set; }
    public string? PAN { get; set; }
    public string? StateName { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public string? TaxType { get; set; }
    public string? HsnCode { get; set; }
    public decimal? GstRate { get; set; }
    public long AlterId { get; set; }
}

public class TallyVoucherDto
{
    public string Guid { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string VoucherType { get; set; } = string.Empty;
    public DateTime VoucherDate { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public string? Narration { get; set; }
    public decimal TotalAmount { get; set; }
    public bool IsCancelled { get; set; }
    public bool IsOptional { get; set; }
    public string? PartyLedgerName { get; set; }
    public long AlterId { get; set; }
    public List<TallyVoucherEntryDto> Entries { get; set; } = new();
}

public class TallyVoucherEntryDto
{
    public string LedgerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsDebit { get; set; }
    public string? BillRefType { get; set; }
    public string? BillName { get; set; }
}

public record TallyRawResponse(
    bool IsSuccess,
    int HttpStatusCode,
    string Content,
    long LatencyMs,
    string? ErrorMessage = null,
    string? TallyErrorCode = null);
