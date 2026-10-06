using System;

namespace TallyAuditAssistant.Core.Domain.Audit;

public class AuditQuery
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PlanId { get; set; } = string.Empty;
    public string QueryNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string AuditArea { get; set; } = "General";
    public string? FindingId { get; set; }
    public string ResponsiblePerson { get; set; } = string.Empty;
    public string Priority { get; set; } = "Medium";
    public string Status { get; set; } = "Open";
    public DateTime QueryDate { get; set; } = DateTime.UtcNow;
    public DateTime? ResponseDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string ManagementResponse { get; set; } = string.Empty;
    public string AuditorRemarks { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
