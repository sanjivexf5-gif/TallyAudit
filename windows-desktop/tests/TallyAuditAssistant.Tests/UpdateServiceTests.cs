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
        Assert.Equal("1.0.0", service.GetCurrentVersion());
    }

    [Fact]
    public async Task CheckForUpdates_WhenNewerVersionExists_SetsIsUpdateAvailableTrue()
    {
        var jsonResponse = @"{
            ""tag_name"": ""v1.1.0"",
            ""name"": ""Release 1.1.0"",
            ""body"": ""New features included"",
            ""html_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/v1.1.0"",
            ""assets"": [
                {
                    ""name"": ""TallyAuditAssistant-Setup.exe"",
                    ""browser_download_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/download/v1.1.0/TallyAuditAssistant-Setup.exe"",
                    ""size"": 15420000
                }
            ]
        }";

        var mockHandler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        var service = new UpdateService(NullLogger<UpdateService>.Instance, httpClient);

        var update = await service.CheckForUpdatesAsync();

        Assert.NotNull(update);
        Assert.True(update.IsUpdateAvailable);
        Assert.Equal("1.1.0", update.LatestVersion);
        Assert.Equal("New features included", update.ReleaseNotes);
        Assert.Equal("https://github.com/sanjivexf5-gif/TallyAudit/releases/download/v1.1.0/TallyAuditAssistant-Setup.exe", update.DownloadUrl);
        Assert.Equal(15420000, update.FileSizeBytes);
    }

    [Fact]
    public async Task CheckForUpdates_WhenSameVersion_SetsIsUpdateAvailableFalse()
    {
        var jsonResponse = @"{
            ""tag_name"": ""v1.0.0"",
            ""name"": ""Release 1.0.0"",
            ""body"": ""Initial Release"",
            ""html_url"": ""https://github.com/sanjivexf5-gif/TallyAudit/releases/v1.0.0"",
            ""assets"": []
        }";

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
            
            // Empty expected checksum should return true for non-empty file
            var resultNoChecksum = await service.VerifyUpdatePackageAsync(tempFile, "");
            Assert.True(resultNoChecksum);

            // Invalid file path should return false
            var resultNonExistent = await service.VerifyUpdatePackageAsync("C:\\NonExistentPathFile.exe", "");
            Assert.False(resultNonExistent);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
