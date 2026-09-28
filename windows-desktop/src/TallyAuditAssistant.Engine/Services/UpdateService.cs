using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Common;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Core.Licensing;

namespace TallyAuditAssistant.Engine.Services;

public class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateService> _logger;
    private const string GitHubRepo = "sanjivexf5-gif/TallyAudit";

    public UpdateService(ILogger<UpdateService> logger, HttpClient? httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "TallyAuditAssistant-App");
        }
    }

    public string GetCurrentVersion() => AppVersion.Version;

    public async Task<UpdateInfo> CheckForUpdatesAsync(bool allowPreRelease = false, CancellationToken ct = default)
    {
        _logger.LogInformation("Checking for updates for {ApplicationName} (Current version: {Version})", AppVersion.ApplicationName, AppVersion.Version);

        var update = new UpdateInfo
        {
            CurrentVersion = AppVersion.Version,
            LatestVersion = AppVersion.Version,
            IsUpdateAvailable = false,
            ReleaseNotes = "You are using the latest version.",
            DownloadUrl = $"https://github.com/{GitHubRepo}/releases/latest",
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            var url = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub release check returned HTTP status {StatusCode}", response.StatusCode);
                update.ReleaseNotes = "Unable to check for updates. You can continue using the current version.";
                return update;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
            var htmlUrl = root.TryGetProperty("html_url", out var htmlUrlProp) ? htmlUrlProp.GetString() ?? update.DownloadUrl : update.DownloadUrl;

            var latestVerStr = tagName.TrimStart('v', 'V').Trim();
            if (string.IsNullOrEmpty(latestVerStr))
            {
                latestVerStr = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString()?.TrimStart('v', 'V').Trim() ?? AppVersion.Version : AppVersion.Version;
            }

            update.LatestVersion = latestVerStr;
            update.ReleaseNotes = !string.IsNullOrWhiteSpace(body) ? body : $"Release {tagName}";
            update.DownloadUrl = htmlUrl;

            // Find installer asset (.exe)
            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        if (asset.TryGetProperty("browser_download_url", out var dlProp))
                        {
                            update.DownloadUrl = dlProp.GetString() ?? update.DownloadUrl;
                        }
                        if (asset.TryGetProperty("size", out var sizeProp) && sizeProp.TryGetInt64(out var sizeVal))
                        {
                            update.FileSizeBytes = sizeVal;
                        }
                        break;
                    }
                }
            }

            // Compare versions
            if (Version.TryParse(latestVerStr, out var latestVer) && Version.TryParse(AppVersion.Version, out var curVer))
            {
                update.IsUpdateAvailable = latestVer > curVer;
            }
            else
            {
                update.IsUpdateAvailable = !string.Equals(latestVerStr, AppVersion.Version, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates from GitHub release feed");
            update.ReleaseNotes = "Unable to check for updates. You can continue using the current version.";
        }

        return update;
    }

    public async Task<string?> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(updateInfo);

        if (string.IsNullOrWhiteSpace(updateInfo.DownloadUrl))
        {
            throw new InvalidOperationException("Download URL is empty.");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "TallyAuditAssistantUpdate");
        Directory.CreateDirectory(tempDir);
        var targetFile = Path.Combine(tempDir, "TallyAuditAssistant-Setup.exe");

        _logger.LogInformation("Downloading update from {Url} to {Path}", updateInfo.DownloadUrl, targetFile);

        using var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? (updateInfo.FileSizeBytes > 0 ? updateInfo.FileSizeBytes : -1L);
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;
            if (totalBytes > 0 && progress != null)
            {
                progress.Report((double)totalRead / totalBytes * 100);
            }
        }

        return targetFile;
    }

    public async Task<bool> VerifyUpdatePackageAsync(string filePath, string expectedChecksum, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return false;

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0)
            return false;

        if (string.IsNullOrWhiteSpace(expectedChecksum))
            return true;

        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream, ct);
        var computedHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        return string.Equals(computedHash, expectedChecksum.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
    }

    public Task<bool> LaunchInstallerAndExitAsync(string installerFilePath)
    {
        if (!File.Exists(installerFilePath))
            return Task.FromResult(false);

        _logger.LogInformation("Launching installer: {FilePath}", installerFilePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = installerFilePath,
            UseShellExecute = true
        };

        Process.Start(startInfo);
        return Task.FromResult(true);
    }
}
