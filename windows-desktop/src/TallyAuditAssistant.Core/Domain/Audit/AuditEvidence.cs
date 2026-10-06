using System;

namespace TallyAuditAssistant.Core.Domain.Audit;

public class AuditEvidence
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PlanId { get; set; } = string.Empty;
    public string AuditArea { get; set; } = "General";
    public string? ProcedureId { get; set; }
    public string? FindingId { get; set; }
    public string EvidenceType { get; set; } = "Document";
    public string Description { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public string? FileHash { get; set; }
    public long? FileSizeBytes { get; set; }
    public DateTime? DateReceived { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Received";
    public string AuditorRemarks { get; set; } = string.Empty;
}