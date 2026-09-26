namespace TallyAuditAssistant.Core.Domain.Backup;

public class BackupMetadata
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public int RecordCount { get; set; }
    public string TriggerReason { get; set; } = "Manual Backup";
    public string ChecksumSha256 { get; set; } = string.Empty;
    public bool IsValidated { get; set; } = true;
}
