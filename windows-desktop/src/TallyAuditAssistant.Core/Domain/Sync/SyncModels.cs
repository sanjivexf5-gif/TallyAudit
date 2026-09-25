namespace TallyAuditAssistant.Core.Domain.Sync;

public enum SyncStage
{
    Idle,
    Connect,
    SelectCompany,
    ReadMasters,
    ReadTransactions,
    Validate,
    Store,
    Index,
    Complete,
    Paused,
    Cancelled,
    Failed
}

public enum SyncMode
{
    Full,
    Incremental
}

public enum SyncStatus
{
    Idle,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public class SyncMetrics
{
    public SyncStage CurrentStage { get; set; } = SyncStage.Idle;
    public string CurrentTaskDescription { get; set; } = "Ready";
    public int RecordsDiscovered { get; set; }
    public int RecordsProcessed { get; set; }
    public int RecordsInserted { get; set; }
    public int RecordsUpdated { get; set; }
    public int RecordsSkipped { get; set; }
    public int Errors { get; set; }
    public TimeSpan ElapsedTime { get; set; } = TimeSpan.Zero;
    public double ProgressPercentage { get; set; }
    public double ItemsPerSecond { get; set; }
}

public class SyncHistoryRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CompanyId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string SyncType { get; set; } = "Full"; // Full or Incremental
    public int VouchersFetched { get; set; }
    public int MastersFetched { get; set; }
    public long DurationMs { get; set; }
    public string Status { get; set; } = "Success"; // Success, Failed, Cancelled
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public record SyncResult(
    bool IsSuccess,
    SyncMode Mode,
    int TotalProcessed,
    int Inserted,
    int Updated,
    int Skipped,
    int Errors,
    TimeSpan Duration,
    string? ErrorMessage = null);
