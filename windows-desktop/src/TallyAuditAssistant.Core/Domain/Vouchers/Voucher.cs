namespace TallyAuditAssistant.Core.Domain.Vouchers;

public class VoucherType
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CompanyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ParentType { get; set; } = string.Empty;
    public string NumberingMethod { get; set; } = "Automatic";
}

public class Voucher
{
    public string Id { get; set; } = Guid.NewGuid().ToString(); // Tally GUID or RemoteID
    public string CompanyId { get; set; } = string.Empty;
    public string VoucherTypeId { get; set; } = string.Empty;
    public string VoucherTypeName { get; set; } = string.Empty;
    public string? VoucherNumber { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime VoucherDate { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public string? Narration { get; set; }
    public decimal TotalAmount { get; set; }
    public bool IsCancelled { get; set; }
    public bool IsOptional { get; set; }
    public string? PartyLedgerName { get; set; }
    public long AlterId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<VoucherEntry> Entries { get; set; } = new();
}

public class VoucherEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string VoucherId { get; set; } = string.Empty;
    public string LedgerName { get; set; } = string.Empty;
    public decimal Amount { get; set; } // Positive for Debit, Negative for Credit
    public bool IsDebit { get; set; }
    public string? BillRefType { get; set; }
    public string? BillName { get; set; }
}
