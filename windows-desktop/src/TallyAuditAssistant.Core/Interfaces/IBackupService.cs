using TallyAuditAssistant.Core.Domain.Backup;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IBackupService
{
    string BackupDirectoryPath { get; set; }
    Task<BackupMetadata> CreateBackupAsync(string triggerReason, CancellationToken ct = default);
    Task<bool> RestoreBackupAsync(string backupFilePath, CancellationToken ct = default);
    Task<IReadOnlyList<BackupMetadata>> GetBackupHistoryAsync(CancellationToken ct = default);
    Task<bool> ValidateBackupIntegrityAsync(string backupFilePath, CancellationToken ct = default);
}
