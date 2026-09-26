namespace TallyAuditAssistant.Core.Interfaces;

public class DatabaseMigrationInfo
{
    public string MigrationId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    public string Checksum { get; set; } = string.Empty;
}

public interface IMigrationService
{
    Task<IReadOnlyList<DatabaseMigrationInfo>> GetAppliedMigrationsAsync(CancellationToken ct = default);
    Task<bool> ApplyPendingMigrationsAsync(CancellationToken ct = default);
    Task<string> GetCurrentSchemaVersionAsync(CancellationToken ct = default);
}
