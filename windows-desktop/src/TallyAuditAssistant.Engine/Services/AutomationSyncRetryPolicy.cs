using TallyAuditAssistant.Core.Domain.Sync;

namespace TallyAuditAssistant.Engine.Services;

/// <summary>
/// Conservative retry policy for automation. Only transient connection failures
/// at the initial sync connection stage are retried; once synchronization has
/// entered a data stage, the workflow is not restarted automatically.
/// </summary>
public static class AutomationSyncRetryPolicy
{
    public const int MaximumAttempts = 3;

    public static bool ShouldRetry(SyncResult result, int attempt)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess ||
            attempt < 1 ||
            attempt >= MaximumAttempts ||
            result.TotalProcessed != 0 ||
            !string.Equals(result.FailedStage, SyncStage.Connect.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsTransientConnectionFailure(result.ErrorMessage);
    }

    public static TimeSpan GetDelayBeforeRetry(int failedAttempt)
    {
        if (failedAttempt < 1 || failedAttempt >= MaximumAttempts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failedAttempt),
                $"Failed attempt must be between 1 and {MaximumAttempts - 1}.");
        }

        // Bounded exponential backoff: 2 seconds, then 4 seconds.
        return TimeSpan.FromSeconds(2 * failedAttempt);
    }

    public static bool IsTransientConnectionFailure(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return false;
        }

        var message = errorMessage.ToLowerInvariant();
        return message.Contains("connection", StringComparison.Ordinal) ||
               message.Contains("connect", StringComparison.Ordinal) ||
               message.Contains("timeout", StringComparison.Ordinal) ||
               message.Contains("timed out", StringComparison.Ordinal) ||
               message.Contains("temporar", StringComparison.Ordinal) ||
               message.Contains("server busy", StringComparison.Ordinal) ||
               message.Contains("connection refused", StringComparison.Ordinal) ||
               message.Contains("connection reset", StringComparison.Ordinal) ||
               message.Contains("http 5", StringComparison.Ordinal) ||
               message.Contains("http status 5", StringComparison.Ordinal) ||
               message.Contains("unable to allocate resources", StringComparison.Ordinal);
    }
}
