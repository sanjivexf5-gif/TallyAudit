namespace TallyAuditAssistant.Core.Domain.Companies;

public class Company
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TallyCompanyName { get; set; } = string.Empty;
    public string? FormalName { get; set; }
    public string? GSTIN { get; set; }
    public string? PAN { get; set; }
    public string? StateName { get; set; }
    public string? StateCode { get; set; }
    public DateTime BooksFromDate { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncDate { get; set; }
    public long LastAlterId { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class FinancialYear
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CompanyId { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsAudited { get; set; } = false;
}
