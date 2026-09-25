using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.TallyIntegration;

public class TallyConnectionMonitor : IDisposable
{
    private readonly ITallyConnection _connection;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TallyConnectionMonitor> _logger;
    private CancellationTokenSource? _cts;
    private Task? _monitoringTask;
    private bool _isDisposed;

    public event EventHandler<TallyEndpointInfo?>? EndpointChanged;
    public event EventHandler<ConnectionStatus>? StatusChanged;

    public bool IsMonitoring => _monitoringTask != null && !_monitoringTask.IsCompleted;
    public ConnectionStatus CurrentStatus { get; private set; } = ConnectionStatus.Disconnected;
    public TallyEndpointInfo? CurrentEndpoint { get; private set; }

    public TallyConnectionMonitor(
        ITallyConnection connection,
        ISettingsService settingsService,
        ILogger<TallyConnectionMonitor> logger)
    {
        _connection = connection;
        _settingsService = settingsService;
        _logger = logger;
    }

    public void StartMonitoring(int normalIntervalSeconds = 15, int backoffIntervalSeconds = 30)
    {
        if (IsMonitoring) return;

        _cts = new CancellationTokenSource();
        _monitoringTask = Task.Run(() => MonitorLoopAsync(normalIntervalSeconds, backoffIntervalSeconds, _cts.Token));
        _logger.LogInformation("Tally automatic connection monitor started (Normal Interval: {Normal}s, Backoff: {Backoff}s)", normalIntervalSeconds, backoffIntervalSeconds);
    }

    public void StopMonitoring()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            _logger.LogInformation("Tally automatic connection monitor stopping...");
        }
    }

    private async Task MonitorLoopAsync(int normalIntervalSeconds, int backoffIntervalSeconds, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var host = await _settingsService.GetTallyHostAsync();
                var port = await _settingsService.GetTallyPortAsync();

                // 1. Lightweight quick check on known configured port first to save CPU
                var isQuickReachable = await _connection.TestConnectionAsync(host, port, cancellationToken);
                
                if (isQuickReachable)
                {
                    UpdateState(ConnectionStatus.Connected, _connection.ActiveEndpoint);
                    await Task.Delay(TimeSpan.FromSeconds(normalIntervalSeconds), cancellationToken);
                    continue;
                }

                // 2. Check if process is even running
                var isProcessRunning = await _connection.CheckIfProcessRunningAsync(cancellationToken);
                if (!isProcessRunning)
                {
                    UpdateState(ConnectionStatus.Disconnected, null);
                    // Process not running; sleep longer with backoff to minimize CPU
                    await Task.Delay(TimeSpan.FromSeconds(backoffIntervalSeconds), cancellationToken);
                    continue;
                }

                // 3. Process is running, scan ports 9000-9005
                var endpoint = await _connection.ProbePortRangeAsync(host, 9000, 9005, cancellationToken);
                if (endpoint != null && endpoint.IsResponsive)
                {
                    UpdateState(ConnectionStatus.Connected, endpoint);
                    await Task.Delay(TimeSpan.FromSeconds(normalIntervalSeconds), cancellationToken);
                }
                else
                {
                    UpdateState(ConnectionStatus.ProcessRunningPortClosed, null);
                    await Task.Delay(TimeSpan.FromSeconds(backoffIntervalSeconds), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error in Tally connection monitor loop");
                await Task.Delay(TimeSpan.FromSeconds(backoffIntervalSeconds), cancellationToken);
            }
        }
    }

    private void UpdateState(ConnectionStatus newStatus, TallyEndpointInfo? newEndpoint)
    {
        var statusChanged = CurrentStatus != newStatus;
        var endpointChanged = CurrentEndpoint?.Port != newEndpoint?.Port || CurrentEndpoint?.Host != newEndpoint?.Host;

        CurrentStatus = newStatus;
        CurrentEndpoint = newEndpoint;

        if (statusChanged)
        {
            _logger.LogInformation("Tally connection state changed to: {Status}", newStatus);
            StatusChanged?.Invoke(this, newStatus);
        }

        if (endpointChanged)
        {
            EndpointChanged?.Invoke(this, newEndpoint);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopMonitoring();
        _cts?.Dispose();
    }
}
