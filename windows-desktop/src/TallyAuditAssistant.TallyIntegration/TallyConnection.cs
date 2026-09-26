using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.TallyIntegration;

public class TallyConnection : ITallyConnection
{
    private readonly ITallyClient _tallyClient;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TallyConnection> _logger;

    public ConnectionStatus CurrentStatus { get; private set; } = ConnectionStatus.Disconnected;
    public TallyEndpointInfo? ActiveEndpoint { get; private set; }
    public string? LastErrorMessage { get; private set; }

    public event EventHandler<ConnectionStatus>? StatusChanged;

    public TallyConnection(ITallyClient tallyClient, ISettingsService settingsService, ILogger<TallyConnection> logger)
    {
        _tallyClient = tallyClient;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<bool> CheckIfProcessRunningAsync(CancellationToken cancellationToken = default)
    {
        if (await _settingsService.IsMockModeEnabledAsync())
        {
            _logger.LogDebug("Mock Mode is enabled: Simulating running TallyPrime process");
            return true;
        }

        try
        {
            var processes = Process.GetProcessesByName("tally");
            var isRunning = processes.Length > 0;
            _logger.LogDebug("Tally process check: {Count} instance(s) found", processes.Length);
            return isRunning;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inspect system process table");
            return false;
        }
    }

    public async Task<TallyEndpointInfo?> ProbePortRangeAsync(string host = "localhost", int startPort = 9000, int endPort = 9005, CancellationToken cancellationToken = default)
    {
        SetStatus(ConnectionStatus.Scanning);
        _logger.LogInformation("Probing Tally endpoints on {Host} across ports {Start}-{End}...", host, startPort, endPort);

        var isProcessRunning = await CheckIfProcessRunningAsync(cancellationToken);

        for (var port = startPort; port <= endPort; port++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = $"http://{host}:{port}";

            try
            {
                var sw = Stopwatch.StartNew();
                var responsive = await _tallyClient.PingAsync(url, cancellationToken);
                sw.Stop();

                if (responsive)
                {
                    _logger.LogInformation("Successfully connected to TallyPrime at {Url} in {Elapsed}ms", url, sw.ElapsedMilliseconds);
                    
                    ActiveEndpoint = new TallyEndpointInfo(
                        Host: host,
                        Port: port,
                        IsResponsive: true,
                        ServerVersion: "TallyPrime 4.x / 5.x Server",
                        LatencyMs: sw.ElapsedMilliseconds
                    );
                    
                    SetStatus(ConnectionStatus.Connected);
                    return ActiveEndpoint;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Port {Port} ping check failed: {Message}", port, ex.Message);
            }
        }

        ActiveEndpoint = null;
        if (isProcessRunning)
        {
            LastErrorMessage = "TallyPrime process is running, but the HTTP server is not responding on ports 9000-9005. Check F12: Advanced Configuration in Tally.";
            SetStatus(ConnectionStatus.ProcessRunningPortClosed);
        }
        else
        {
            LastErrorMessage = "TallyPrime is not running on this computer.";
            SetStatus(ConnectionStatus.Disconnected);
        }

        return null;
    }

    public async Task<bool> TestConnectionAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var url = $"http://{host}:{port}";
        var responsive = await _tallyClient.PingAsync(url, cancellationToken);
        if (responsive)
        {
            ActiveEndpoint = new TallyEndpointInfo(host, port, true);
            SetStatus(ConnectionStatus.Connected);
            return true;
        }

        SetStatus(ConnectionStatus.Disconnected);
        return false;
    }

    private void SetStatus(ConnectionStatus newStatus)
    {
        if (CurrentStatus != newStatus)
        {
            CurrentStatus = newStatus;
            StatusChanged?.Invoke(this, newStatus);
        }
    }
}
