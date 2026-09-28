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
        Assert.Equal("1.0.1", service.GetCurrentVersion());
    }

    [Fact]
    public async Task CheckForUpdates_WhenNewerVersionExists_SetsIsUpdateAvailableTrue()
    {
        var jsonResponse = @"[
            {
                ""tag_name"": ""v1.0.2"",
                ""name"": ""Release 1.0.2"",
                ""draft"": false,
                ""prerelease"": false,
                ""body"": ""New features included"",
                ""html_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/v1.0.2"",
                ""assets"": [
                    {
                        ""name"": ""TallyAuditAssistant-Setup.exe"",
                        ""browser_download_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/download/v1.0.2/TallyAuditAssistant-Setup.exe"",
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
        var service = new UpdateService(NullLogger<UpdateService>.Instance, httpClient);

        var update = await service.CheckForUpdatesAsync();

        Assert.NotNull(update);
        Assert.True(update.IsUpdateAvailable);
        Assert.Equal("1.0.2", update.LatestVersion);
        Assert.Equal("New features included", update.ReleaseNotes);
        Assert.Equal("https://github.com/sanjivexf5-gif/TallyAudit/releases/download/v1.0.2/TallyAuditAssistant-Setup.exe", update.DownloadUrl);
        Assert.Equal(15420000, update.FileSizeBytes);
    }

    [Fact]
    public async Task CheckForUpdates_WhenSameVersion_SetsIsUpdateAvailableFalse()
    {
        var jsonResponse = @"[
            {
                ""tag_name"": ""v1.0.1"",
                ""name"": ""Release 1.0.1"",
                ""draft"": false,
                ""prerelease"": false,
                ""body"": ""Current Release"",
                ""html_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/v1.0.1"",
                ""assets"": []
            }
        ]";

        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        var service = new UpdateService(NullLogger<UpdateService>.Instance, httpClient);

        var update = await service.CheckForUpdatesAsync();

        Assert.NotNull(update);
        Assert.False(update.IsUpdateAvailable);
        Assert.Equal("1.0.1", update.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdates_WhenDowngrade_SetsIsUpdateAvailableFalse()
    {
        var jsonResponse = @"[
            {
                ""tag_name"": ""v1.0.0"",
                ""name"": ""Release 1.0.0"",
                ""draft"": false,
                ""prerelease"": false,
                ""body"": ""Older Release"",
                ""html_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/v1.0.0"",
                ""assets"": []
            }
        ]";

        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        var service = new UpdateService(NullLogger<UpdateService>.Instance, httpClient);

        var update = await service.CheckForUpdatesAsync();

        Assert.NotNull(update);
        Assert.False(update.IsUpdateAvailable);
        Assert.Equal("1.0.0", update.LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdates_SemanticVersion1010_IsNewerThan109()
    {
        var jsonResponse = @"[
            {
                ""tag_name"": ""v1.0.10"",
                ""name"": ""Release 1.0.10"",
                ""draft"": false,
                ""prerelease"": false,
                ""body"": ""Build 10"",
                ""html_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/v1.0.10"",
                ""assets"": []
            }
        ]";

        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        var service = new UpdateService(NullLogger<UpdateService>.Instance, httpClient);

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
                ""body"": ""Draft build""
            },
            {
                ""tag_name"": ""v1.5.0-beta"",
                ""name"": ""Release 1.5.0 Beta"",
                ""draft"": false,
                ""prerelease"": true,
                ""body"": ""Beta build""
            },
            {
                ""tag_name"": ""v1.0.1"",
                ""name"": ""Release 1.0.1 Stable"",
                ""draft"": false,
                ""prerelease"": false,
                ""body"": ""Current stable""
            }
        ]";

        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        var service = new UpdateService(NullLogger<UpdateService>.Instance, httpClient);

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
