namespace TallyAuditAssistant.Core.Domain.Audit;

public class AuditTrailEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Username { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Category { get; set; } = "SYSTEM";
    public string? CompanyId { get; set; }
    public string? FinancialPeriodId { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public string IntegrityHash { get; set; } = string.Empty;
}
