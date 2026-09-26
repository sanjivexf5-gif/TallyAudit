using TallyAuditAssistant.Core.Licensing;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IUpdateService
{
    Task<UpdateInfo> CheckForUpdatesAsync(bool allowPreRelease = false, CancellationToken ct = default);
    Task<bool> VerifyUpdatePackageAsync(string filePath, string expectedChecksum, CancellationToken ct = default);
    string GetCurrentVersion();
}
