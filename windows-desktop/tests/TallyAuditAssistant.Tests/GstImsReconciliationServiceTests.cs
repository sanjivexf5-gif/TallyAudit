using TallyAuditAssistant.Data;
using TallyAuditAssistant.Engine.Gst;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class GstImsReconciliationServiceTests
{
    [Fact]
    public async Task ReviewCsvAsync_RejectsOversizedFileBeforeOpeningDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ims-{Guid.NewGuid():N}.csv");
        try
        {
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.SetLength(GstImsReconciliationService.MaxCsvFileSizeBytes + 1);

            var service = new GstImsReconciliationService(
                new SqliteConnectionFactory(Path.Combine(Path.GetTempPath(), $"ims-db-{Guid.NewGuid():N}.db")));

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.ReviewCsvAsync("company-1", new DateTime(2026, 4, 1), new DateTime(2026, 4, 30), path));

            Assert.Contains("25 MB", exception.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
