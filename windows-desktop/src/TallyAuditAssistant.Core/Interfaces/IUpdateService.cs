using System;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Licensing;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IUpdateService
{
    Task<UpdateInfo> CheckForUpdatesAsync(bool allowPreRelease = false, CancellationToken ct = default);
    Task<bool> VerifyUpdatePackageAsync(string filePath, string expectedChecksum, CancellationToken ct = default);
    Task<string?> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress = null, CancellationToken ct = default);
    Task<bool> LaunchInstallerAndExitAsync(string installerFilePath);
    string GetCurrentVersion();
}
