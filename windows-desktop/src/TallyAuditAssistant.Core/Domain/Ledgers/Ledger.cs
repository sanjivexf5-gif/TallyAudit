namespace TallyAuditAssistant.Core.Domain.Ledgers;

public class Group
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CompanyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentName { get; set; }
    public string? PrimaryGroup { get; set; }
    public long? AlterId { get; set; }
}

public class Ledger
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CompanyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ParentGroup { get; set; } = string.Empty;
    public string? GSTIN { get; set; }
    public string? PAN { get; set; }
    public string? StateName { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public bool IsBillWise { get; set; }
    public string? TaxType { get; set; }
    public string? HsnCode { get; set; }
    public decimal? GstRate { get; set; }
    public long? AlterId { get; set; }
}
