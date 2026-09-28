using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Common;
using TallyAuditAssistant.Core.Licensing;
using TallyAuditAssistant.Engine.Services;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class UpdateServiceTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }

    [Fact]
    public void GetCurrentVersion_ReturnsVersionString()
    {
        var service = new UpdateService(NullLogger<UpdateService>.Instance);
        Assert.Equal("1.0.2", service.GetCurrentVersion());
    }

    [Fact]
    public async Task CheckForUpdates_Installed101_GitHubRelease102_UpdateAvailableTrue()
    {
        // Test: installed = 1.0.1, GitHub release: tag_name = v1.0.2, asset = TallyAuditAssistant-Setup-1.0.2.exe
        // Expected: update available = true
        var jsonResponse = @"[
            {
                ""tag_name"": ""v1.0.2"",
                ""name"": ""Tally Audit Assistant v1.0.2"",
                ""draft"": false,
                ""prerelease"": false,
                ""body"": ""Workflow context corrections and reports enhancement"",
                ""html_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/tag/v1.0.2"",
                ""assets"": [
                    {
                        ""name"": ""TallyAuditAssistant-Setup-1.0.2.exe"",
                        ""browser_download_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/download/v1.0.2/TallyAuditAssistant-Setup-1.0.2.exe"",
                        ""size"": 15500000
                    }
                ]
            }
        ]";

        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        var service = new UpdateService(NullLogger<UpdateService>.Instance, httpClient, installedVersion: "1.0.1");

        var update = await service.CheckForUpdatesAsync();

        Assert.NotNull(update);
        Assert.True(update.IsUpdateAvailable);
        Assert.Equal("1.0.2", update.LatestVersion);
        Assert.Equal("1.0.1", update.CurrentVersion);
        Assert.Equal("https://github.com/sanjivexf5-gif/TallyAudit/releases/download/v1.0.2/TallyAuditAssistant-Setup-1.0.2.exe", update.DownloadUrl);
        Assert.Equal(15500000, update.FileSizeBytes);
    }

    [Fact]
    public async Task CheckForUpdates_Installed100_GitHubRelease101_UpdateAvailableTrue()
    {
        // Test: installed = 1.0.0, GitHub release: tag_name = v1.0.1, asset = TallyAuditAssistant-Setup-1.0.1.exe
        // Expected: update available = true
        var jsonResponse = @"[
            {
                ""tag_name"": ""v1.0.1"",
                ""name"": ""Tally Audit Assistant v1.0.1"",
                ""draft"": false,
                ""prerelease"": false,
                ""body"": ""Fix audit foreign key integrity and rule catalog"",
                ""html_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/tag/v1.0.1"",
                ""assets"": [
                    {
                        ""name"": ""TallyAuditAssistant-Setup-1.0.1.exe"",
                        ""browser_download_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/download/v1.0.1/TallyAuditAssistant-Setup-1.0.1.exe"",
                        ""size"": 15420000
                    }
                ]
            }
        ]";

        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        var service = new UpdateService(NullLogger<UpdateService>.Instance, httpClient, installedVersion: "1.0.0");

        var update = await service.CheckForUpdatesAsync();

        Assert.NotNull(update);
        Assert.True(update.IsUpdateAvailable);
        Assert.Equal("1.0.1", update.LatestVersion);
        Assert.Equal("1.0.0", update.CurrentVersion);
        Assert.Equal("https://github.com/sanjivexf5-gif/TallyAudit/releases/download/v1.0.1/TallyAuditAssistant-Setup-1.0.1.exe", update.DownloadUrl);
        Assert.Equal(15420000, update.FileSizeBytes);
    }

    [Fact]
    public async Task CheckForUpdates_Installed101_GitHubRelease101_UpdateAvailableFalse()
    {
        // Test: installed = 1.0.1, GitHub: v1.0.1
        // Expected: update available = false
        var jsonResponse = @"[
            {
                ""tag_name"": ""v1.0.1"",
                ""name"": ""Tally Audit Assistant v1.0.1"",
                ""draft"": false,
                ""prerelease"": false,
                ""body"": ""Current release"",
                ""html_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/tag/v1.0.1"",
                ""assets"": [
                    {
                        ""name"": ""TallyAuditAssistant-Setup-1.0.1.exe"",
                        ""browser_download_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/download/v1.0.1/TallyAuditAssistant-Setup-1.0.1.exe"",
                        ""size"": 15420000
                    }
                ]
            }
        ]";

        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        var service = new UpdateService(NullLogger<UpdateService>.Instance, httpClient, installedVersion: "1.0.1");

        var update = await service.CheckForUpdatesAsync();

        Assert.NotNull(update);
        Assert.False(update.IsUpdateAvailable);
        Assert.Equal("1.0.1", update.LatestVersion);
        Assert.Equal("1.0.1", update.CurrentVersion);
    }

    [Fact]
    public async Task CheckForUpdates_Installed101_GitHubRelease100_UpdateAvailableFalse()
    {
        // Test: installed = 1.0.1, GitHub: v1.0.0
        // Expected: update available = false (no downgrade)
        var jsonResponse = @"[
            {
                ""tag_name"": ""v1.0.0"",
                ""name"": ""Tally Audit Assistant v1.0.0"",
                ""draft"": false,
                ""prerelease"": false,
                ""body"": ""Older release"",
                ""html_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/tag/v1.0.0"",
                ""assets"": [
                    {
                        ""name"": ""TallyAuditAssistant-Setup-1.0.0.exe"",
                        ""browser_download_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/download/v1.0.0/TallyAuditAssistant-Setup-1.0.0.exe"",
                        ""size"": 15000000
                    }
                ]
            }
        ]";

        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        var service = new UpdateService(NullLogger<UpdateService>.Instance, httpClient, installedVersion: "1.0.1");

        var update = await service.CheckForUpdatesAsync();

        Assert.NotNull(update);
        Assert.False(update.IsUpdateAvailable);
        Assert.Equal("1.0.0", update.LatestVersion);
        Assert.Equal("1.0.1", update.CurrentVersion);
    }

    [Fact]
    public async Task CheckForUpdates_ReleaseTag100_WithMismatched101Asset_MustNotBeSelectedAs101()
    {
        // Test: release tag = v1.0.0, asset = TallyAuditAssistant-Setup-1.0.1.exe
        // Expected: INVALID RELEASE / asset pairing mismatch, must NOT be selected as version 1.0.1!
        var jsonResponse = @"[
            {
                ""tag_name"": ""v1.0.0"",
                ""name"": ""Tally Audit Assistant v1.0.0"",
                ""draft"": false,
                ""prerelease"": false,
                ""body"": ""Erroneously contains 1.0.1 asset"",
                ""html_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/tag/v1.0.0"",
                ""assets"": [
                    {
                        ""name"": ""TallyAuditAssistant-Setup-1.0.1.exe"",
                        ""browser_download_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/download/v1.0.0/TallyAuditAssistant-Setup-1.0.1.exe"",
                        ""size"": 15420000
                    }
                ]
            }
        ]";

        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        // Installed version is 1.0.0
        var service = new UpdateService(NullLogger<UpdateService>.Instance, httpClient, installedVersion: "1.0.0");

        var update = await service.CheckForUpdatesAsync();

        Assert.NotNull(update);
        // The mismatched asset in v1.0.0 MUST NOT trigger an update to 1.0.1!
        Assert.False(update.IsUpdateAvailable);
        Assert.NotEqual("1.0.1", update.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdates_SemanticVersion1010_IsNewerThan109()
    {
        var jsonResponse = @"[
            {
                ""tag_name"": ""v1.0.10"",
                ""name"": ""Tally Audit Assistant v1.0.10"",
                ""draft"": false,
                ""prerelease"": false,
                ""body"": ""Build 10"",
                ""html_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/tag/v1.0.10"",
                ""assets"": [
                    {
                        ""name"": ""TallyAuditAssistant-Setup-1.0.10.exe"",
                        ""browser_download_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/download/v1.0.10/TallyAuditAssistant-Setup-1.0.10.exe"",
                        ""size"": 15420000
                    }
                ]
            }
        ]";

        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        var service = new UpdateService(NullLogger<UpdateService>.Instance, httpClient, installedVersion: "1.0.9");

        var update = await service.CheckForUpdatesAsync();

        Assert.NotNull(update);
        Assert.True(update.IsUpdateAvailable);
        Assert.Equal("1.0.10", update.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdates_IgnoresDraftsAndPrereleases()
    {
        var jsonResponse = @"[
            {
                ""tag_name"": ""v2.0.0"",
                ""name"": ""Release 2.0.0 Draft"",
                ""draft"": true,
                ""prerelease"": false,
                ""body"": ""Draft build"",
                ""assets"": [
                    {
                        ""name"": ""TallyAuditAssistant-Setup-2.0.0.exe"",
                        ""browser_download_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/download/v2.0.0/TallyAuditAssistant-Setup-2.0.0.exe"",
                        ""size"": 15420000
                    }
                ]
            },
            {
                ""tag_name"": ""v1.5.0-beta"",
                ""name"": ""Release 1.5.0 Beta"",
                ""draft"": false,
                ""prerelease"": true,
                ""body"": ""Beta build"",
                ""assets"": [
                    {
                        ""name"": ""TallyAuditAssistant-Setup-1.5.0.exe"",
                        ""browser_download_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/download/v1.5.0/TallyAuditAssistant-Setup-1.5.0.exe"",
                        ""size"": 15420000
                    }
                ]
            },
            {
                ""tag_name"": ""v1.0.1"",
                ""name"": ""Release 1.0.1 Stable"",
                ""draft"": false,
                ""prerelease"": false,
                ""body"": ""Current stable"",
                ""assets"": [
                    {
                        ""name"": ""TallyAuditAssistant-Setup-1.0.1.exe"",
                        ""browser_download_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/download/v1.0.1/TallyAuditAssistant-Setup-1.0.1.exe"",
                        ""size"": 15420000
                    }
                ]
            }
        ]";

        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        var service = new UpdateService(NullLogger<UpdateService>.Instance, httpClient, installedVersion: "1.0.1");

        var update = await service.CheckForUpdatesAsync(allowPreRelease: false);

        Assert.NotNull(update);
        Assert.False(update.IsUpdateAvailable);
        Assert.Equal("1.0.1", update.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdates_WhenGitHubUnavailable_ReturnsSafeFallback()
    {
        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var httpClient = new HttpClient(mockHandler);
        var service = new UpdateService(NullLogger<UpdateService>.Instance, httpClient);

        var update = await service.CheckForUpdatesAsync();

        Assert.NotNull(update);
        Assert.False(update.IsUpdateAvailable);
        Assert.Contains("Unable to check for updates", update.ReleaseNotes);
    }

    [Fact]
    public async Task VerifyUpdatePackage_ValidatesFileIntegrity()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Test Package Content");

            var service = new UpdateService(NullLogger<UpdateService>.Instance);
            
            var resultNoChecksum = await service.VerifyUpdatePackageAsync(tempFile, "");
            Assert.True(resultNoChecksum);

            var resultNonExistent = await service.VerifyUpdatePackageAsync("C:\\NonExistentPathFile.exe", "");
            Assert.False(resultNonExistent);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
