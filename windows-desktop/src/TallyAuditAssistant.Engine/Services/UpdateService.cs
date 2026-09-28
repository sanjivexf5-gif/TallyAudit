using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private readonly string _installedVersion;
    private const string GitHubRepo = "sanjivexf5-gif/TallyAudit";

    public UpdateService(ILogger<UpdateService> logger, HttpClient? httpClient = null, string? installedVersion = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _installedVersion = !string.IsNullOrWhiteSpace(installedVersion) ? installedVersion : AppVersion.Version;
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "TallyAuditAssistant-App");
        }
    }

    public string GetCurrentVersion() => _installedVersion;

    public async Task<UpdateInfo> CheckForUpdatesAsync(bool allowPreRelease = false, CancellationToken ct = default)
    {
        _logger.LogInformation("Checking for updates for {ApplicationName} (Current version: {Version})", AppVersion.ApplicationName, _installedVersion);

        var update = new UpdateInfo
        {
            CurrentVersion = _installedVersion,
            LatestVersion = _installedVersion,
            IsUpdateAvailable = false,
            ReleaseNotes = "You are using the latest version of Tally Audit Assistant.",
            DownloadUrl = $"https://github.com/{GitHubRepo}/releases/latest",
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            // First try releases list to check all published releases
            var url = $"https://api.github.com/repos/{GitHubRepo}/releases";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var response = await _httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    EvaluateReleaseArray(doc.RootElement, update, allowPreRelease);
                    return update;
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    EvaluateSingleRelease(doc.RootElement, update, allowPreRelease);
                    return update;
                }
            }

            // Fallback to /releases/latest if array endpoint wasn't processed
            var latestUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
            using var latestRequest = new HttpRequestMessage(HttpMethod.Get, latestUrl);
            latestRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var latestResponse = await _httpClient.SendAsync(latestRequest, ct);
            if (latestResponse.IsSuccessStatusCode)
            {
                var json = await latestResponse.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    EvaluateSingleRelease(doc.RootElement, update, allowPreRelease);
                }
            }
            else
            {
                _logger.LogWarning("GitHub release check returned HTTP status {StatusCode}", latestResponse.StatusCode);
                update.ReleaseNotes = "Unable to check for updates. You can continue using the current version.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates from GitHub release feed");
            update.ReleaseNotes = "Unable to check for updates. You can continue using the current version.";
        }

        return update;
    }

    private void EvaluateReleaseArray(JsonElement releasesArray, UpdateInfo update, bool allowPreRelease)
    {
        Version? highestVersion = null;
        JsonElement? bestRelease = null;
        string bestVersionStr = string.Empty;
        string bestDownloadUrl = string.Empty;
        long bestFileSize = 0;

        foreach (var rel in releasesArray.EnumerateArray())
        {
            var isDraft = rel.TryGetProperty("draft", out var dProp) && dProp.GetBoolean();
            if (isDraft) continue;

            var isPreRelease = rel.TryGetProperty("prerelease", out var prProp) && prProp.GetBoolean();
            if (isPreRelease && !allowPreRelease) continue;

            var tagName = rel.TryGetProperty("tag_name", out var tagP) ? tagP.GetString() ?? "" : "";
            var verStr = tagName.TrimStart('v', 'V').Trim();
            if (string.IsNullOrEmpty(verStr) || !Version.TryParse(verStr, out var parsedVer))
            {
                continue;
            }

            // CRITICAL: Validate release.tag_name and asset.name match the same version!
            if (!TryFindMatchingAsset(rel, verStr, out var downloadUrl, out var fileSize))
            {
                _logger.LogWarning("Release tag {TagName} does not contain a matching installer asset for version {Version}", tagName, verStr);
                continue;
            }

            if (highestVersion == null || parsedVer > highestVersion)
            {
                highestVersion = parsedVer;
                bestRelease = rel;
                bestVersionStr = verStr;
                bestDownloadUrl = downloadUrl;
                bestFileSize = fileSize;
            }
        }

        if (bestRelease.HasValue && highestVersion != null)
        {
            ApplyRelease(bestRelease.Value, bestVersionStr, bestDownloadUrl, bestFileSize, update);
        }
        else
        {
            update.IsUpdateAvailable = false;
        }
    }

    private void EvaluateSingleRelease(JsonElement releaseObj, UpdateInfo update, bool allowPreRelease)
    {
        var isDraft = releaseObj.TryGetProperty("draft", out var dProp) && dProp.GetBoolean();
        if (isDraft) return;

        var isPreRelease = releaseObj.TryGetProperty("prerelease", out var prProp) && prProp.GetBoolean();
        if (isPreRelease && !allowPreRelease) return;

        var tagName = releaseObj.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
        var verStr = tagName.TrimStart('v', 'V').Trim();
        if (string.IsNullOrEmpty(verStr) || !Version.TryParse(verStr, out var parsedVer))
        {
            return;
        }

        update.LatestVersion = verStr;

        if (!TryFindMatchingAsset(releaseObj, verStr, out var downloadUrl, out var fileSize))
        {
            _logger.LogWarning("Single release {TagName} has no matching installer asset pairing for version {Version}", tagName, verStr);
            update.IsUpdateAvailable = false;
            update.ReleaseNotes = $"Release {tagName} contains no valid installer asset pairing for version {verStr}.";
            return;
        }

        ApplyRelease(releaseObj, verStr, downloadUrl, fileSize, update);
    }

    private void ApplyRelease(JsonElement releaseObj, string releaseVersionStr, string downloadUrl, long fileSize, UpdateInfo update)
    {
        var tagName = releaseObj.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
        var body = releaseObj.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
        var htmlUrl = releaseObj.TryGetProperty("html_url", out var htmlUrlProp) ? htmlUrlProp.GetString() ?? update.DownloadUrl : update.DownloadUrl;

        update.LatestVersion = releaseVersionStr;
        update.ReleaseNotes = !string.IsNullOrWhiteSpace(body) ? body : $"Release {tagName}";
        update.DownloadUrl = !string.IsNullOrEmpty(downloadUrl) ? downloadUrl : htmlUrl;
        update.FileSizeBytes = fileSize;

        if (Version.TryParse(releaseVersionStr, out var latestVer) && Version.TryParse(_installedVersion, out var curVer))
        {
            update.IsUpdateAvailable = latestVer > curVer;
        }
        else
        {
            update.IsUpdateAvailable = false;
        }
    }

    public static bool TryFindMatchingAsset(JsonElement releaseObj, string releaseVersionStr, out string downloadUrl, out long fileSize)
    {
        downloadUrl = string.Empty;
        fileSize = 0;

        if (!releaseObj.TryGetProperty("assets", out var assetsProp) || assetsProp.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var asset in assetsProp.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;

            // Validate that the asset name pairs with the release version
            if (IsAssetVersionMatching(name, releaseVersionStr))
            {
                if (asset.TryGetProperty("browser_download_url", out var dlProp))
                {
                    downloadUrl = dlProp.GetString() ?? "";
                }
                if (asset.TryGetProperty("size", out var sizeProp) && sizeProp.TryGetInt64(out var sizeVal))
                {
                    fileSize = sizeVal;
                }
                return !string.IsNullOrEmpty(downloadUrl);
            }
        }

        return false;
    }

    public static bool IsAssetVersionMatching(string assetName, string releaseVersionStr)
    {
        // Extract version numbers from the asset filename (e.g., TallyAuditAssistant-Setup-1.0.1.exe -> 1.0.1)
        var match = Regex.Match(
            assetName, 
            @"(?:Setup[_-]?)v?(\d+\.\d+(?:\.\d+)?(?:\.\d+)?)\.exe$", 
            RegexOptions.IgnoreCase);

        if (match.Success)
        {
            var assetVersion = match.Groups[1].Value;
            return string.Equals(assetVersion, releaseVersionStr, StringComparison.OrdinalIgnoreCase);
        }

        // Generic installer (e.g. TallyAuditAssistant-Setup.exe) without an embedded version
        return assetName.Equals("TallyAuditAssistant-Setup.exe", StringComparison.OrdinalIgnoreCase);
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
