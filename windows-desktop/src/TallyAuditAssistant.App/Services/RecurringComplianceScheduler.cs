using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.Services;

/// <summary>
/// Generates due recurring practice engagements once at startup and then once
/// every 24 hours. It is started only after database initialization completes.
/// </summary>
public sealed class RecurringComplianceScheduler : IAsyncDisposable
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);
    private readonly IPracticeRepository _practiceRepository;
    private readonly ILogger<RecurringComplianceScheduler> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _gate = new();
    private Task? _loopTask;
    private bool _started;

    public RecurringComplianceScheduler(
        IPracticeRepository practiceRepository,
        ILogger<RecurringComplianceScheduler> logger)
    {
        _practiceRepository = practiceRepository;
        _logger = logger;
    }

    public Task StartAsync()
    {
        lock (_gate)
        {
            if (_started)
                return Task.CompletedTask;

            _started = true;
            _loopTask = RunLoopAsync(_shutdown.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? loopTask;
        lock (_gate)
        {
            if (!_started)
                return;

            _shutdown.Cancel();
            loopTask = _loopTask;
            _started = false;
        }

        if (loopTask is not null)
        {
            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal application shutdown.
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        // Run immediately once at startup; the repository operation is
        // transactional and idempotent, so a restart is safe.
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var created = await _practiceRepository.GenerateRecurringEngagementsAsync(
                    DateTime.Today, cancellationToken).ConfigureAwait(false);

                if (created > 0)
                    _logger.LogInformation("Generated {EngagementCount} recurring compliance engagement(s).", created);
                else
                    _logger.LogDebug("Recurring compliance scheduler found no due engagements.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Keep the desktop app available if recurrence generation fails.
                _logger.LogError(ex, "Recurring compliance generation failed; it will retry at the next scheduled run.");
            }

            try
            {
                await Task.Delay(RunInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }
}
