using System;
namespace TallyAuditAssistant.Core.Domain.Audit;
public class WorkingPaper
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PlanId { get; set; } = string.Empty;
    public string AuditArea { get; set; } = "General";
    public string Title { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string ProcedurePerformed { get; set; } = string.Empty;
    public string Conclusion { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public string PreparedBy { get; set; } = Environment.UserName;
    public DateTime PreparedDate { get; set; } = DateTime.Today;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedDate { get; set; }
    public string? RelatedFindingId { get; set; }
    public string CompanyId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
}
public class WorkingPaperAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WorkingPaperId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? FileHash { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public string AddedBy { get; set; } = Environment.UserName;
}