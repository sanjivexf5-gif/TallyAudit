using TallyAuditAssistant.Core.Domain.Sync;
using TallyAuditAssistant.Engine.Services;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class AutomationSyncRetryPolicyTests
{
    private static SyncResult Failure(
        string? message = "Could not connect to TallyPrime on localhost:9000.",
        string? failedStage = "Connect",
        int totalProcessed = 0) =>
        new(false, SyncMode.Incremental, totalProcessed, 0, 0, 0, 1, TimeSpan.FromSeconds(1), message, failedStage);

    [Fact]
    public void RetriesTransientConnectionFailureBeforeAnyDataWasProcessed()
    {
        Assert.True(AutomationSyncRetryPolicy.ShouldRetry(Failure(), attempt: 1));
    }

    [Fact]
    public void RetriesTimeoutAndServerBusyConnectionFailures()
    {
        Assert.True(AutomationSyncRetryPolicy.ShouldRetry(
            Failure("Tally HTTP request timed out.", "Connect"), attempt: 1));
        Assert.True(AutomationSyncRetryPolicy.ShouldRetry(
            Failure("Tally server busy; HTTP 503.", "Connect"), attempt: 1));
    }

    [Fact]
    public void DoesNotRetryAfterMaximumAttempts()
    {
        Assert.False(AutomationSyncRetryPolicy.ShouldRetry(Failure(), attempt: 3));
    }

    [Fact]
    public void DoesNotRetrySuccessfulResult()
    {
        var success = new SyncResult(true, SyncMode.Incremental, 0, 0, 0, 0, 0, TimeSpan.Zero);
        Assert.False(AutomationSyncRetryPolicy.ShouldRetry(success, attempt: 1));
    }

    [Fact]
    public void DoesNotRetryWhenDataWasProcessed()
    {
        Assert.False(AutomationSyncRetryPolicy.ShouldRetry(
            Failure(totalProcessed: 5), attempt: 1));
    }

    [Theory]
    [InlineData("SelectCompany")]
    [InlineData("ReadGroups")]
    [InlineData("ReadVouchers")]
    public void DoesNotRestartSyncAfterConnectionStage(string failedStage)
    {
        Assert.False(AutomationSyncRetryPolicy.ShouldRetry(
            Failure(failedStage: failedStage), attempt: 1));
    }

    [Fact]
    public void DoesNotRetryPermanentCompanyOrSchemaErrors()
    {
        Assert.False(AutomationSyncRetryPolicy.ShouldRetry(
            Failure("Could not load company profile; verify company is open.", "Connect"), attempt: 1));
        Assert.False(AutomationSyncRetryPolicy.ShouldRetry(
            Failure("Unknown symbol found in TDL schema.", "Connect"), attempt: 1));
    }

    [Fact]
    public void BackoffIncreasesAndIsBounded()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), AutomationSyncRetryPolicy.GetDelayBeforeRetry(1));
        Assert.Equal(TimeSpan.FromSeconds(4), AutomationSyncRetryPolicy.GetDelayBeforeRetry(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutomationSyncRetryPolicy.GetDelayBeforeRetry(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutomationSyncRetryPolicy.GetDelayBeforeRetry(3));
    }

    [Fact]
    public void DoesNotRetryWhenStageOrErrorIsMissing()
    {
        Assert.False(AutomationSyncRetryPolicy.ShouldRetry(Failure(failedStage: null), attempt: 1));
        Assert.False(AutomationSyncRetryPolicy.ShouldRetry(Failure(message: null), attempt: 1));
    }
}
