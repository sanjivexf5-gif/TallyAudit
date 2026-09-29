using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Common;
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
        return await DiscoverTallyAsync(host, startPort, endPort, cancellationToken);
    }

    public async Task<TallyEndpointInfo?> DiscoverTallyAsync(string? preferredHost = null, int? preferredPort = null, int scanRangeMax = 9005, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetStatus(ConnectionStatus.Scanning);
        
        var rawHost = preferredHost ?? await _settingsService.GetTallyHostAsync();
        var rawPort = preferredPort ?? await _settingsService.GetTallyPortAsync();

        string host = "localhost";
        int targetPort = 9000;
        try
        {
            var normalized = TallyEndpointNormalization.Normalize(rawHost, rawPort);
            host = normalized.Host;
            targetPort = normalized.Port;
        }
        catch
        {
            host = string.IsNullOrWhiteSpace(rawHost) ? "localhost" : rawHost;
            targetPort = (rawPort > 0 && rawPort <= 65535) ? rawPort : 9000;
        }

        _logger.LogInformation("Attempting primary connection to TallyPrime at {Host}:{Port}...", host, targetPort);
        
        var primaryResult = await TestConnectionDetailedAsync(host, targetPort, cancellationToken);
        if (primaryResult.IsResponsive)
        {
            ActiveEndpoint = primaryResult;
            SetStatus(ConnectionStatus.Connected);
            return primaryResult;
        }

        // 2. Not found at primary, scan common range (9000 to scanRangeMax)
        _logger.LogInformation("Primary port failed. Scanning range 9000-{Max} on {Host}...", scanRangeMax, host);
        
        int rangeCount = Math.Max(0, scanRangeMax - 9000 + 1);
        var portsToScan = Enumerable.Range(9000, rangeCount)
            .Where(p => p != targetPort)
            .ToList();

        var semaphore = new SemaphoreSlim(3); // Probing 3 ports at a time
        var tasks = portsToScan.Select(async port =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await TestConnectionDetailedAsync(host, port, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        var successful = results.FirstOrDefault(r => r.IsResponsive);

        if (successful != null)
        {
            _logger.LogInformation("Tally detected on alternative port {Port}", successful.Port);
            ActiveEndpoint = successful;
            SetStatus(ConnectionStatus.Connected);
            
            // Persist the newly discovered port
            await _settingsService.SetTallyPortAsync(successful.Port);
            return successful;
        }

        // 3. Not found anywhere
        ActiveEndpoint = null;
        var isProcessRunning = await CheckIfProcessRunningAsync(cancellationToken);
        
        if (isProcessRunning)
        {
            LastErrorMessage = $"TallyPrime process detected, but no responsive HTTP server found on ports 9000-{scanRangeMax}. Verify 'Enable HTTP Server' in Tally F12 settings.";
            SetStatus(ConnectionStatus.ProcessRunningPortClosed);
        }
        else
        {
            LastErrorMessage = "TallyPrime is not detected on this system.";
            SetStatus(ConnectionStatus.Disconnected);
        }

        return null;
    }

    public async Task<TallyEndpointInfo> TestConnectionDetailedAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        string normHost = host;
        int normPort = port;
        try
        {
            var normalized = TallyEndpointNormalization.Normalize(host, port);
            normHost = normalized.Host;
            normPort = normalized.Port;
        }
        catch (Exception ex)
        {
            return new TallyEndpointInfo(
                Host: host,
                Port: port,
                IsResponsive: false,
                FailureCause: ConnectionFailureCause.Unknown,
                ErrorMessage: $"Invalid endpoint: {ex.Message}");
        }

        var url = $"http://{normHost}:{normPort}";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            // PingAsync in TallyClient now does body validation
            var responsive = await _tallyClient.PingAsync(url, cancellationToken);
            sw.Stop();

            if (responsive)
            {
                return new TallyEndpointInfo(
                    Host: normHost,
                    Port: normPort,
                    IsResponsive: true,
                    ServerVersion: "TallyPrime",
                    LatencyMs: sw.ElapsedMilliseconds
                );
            }

            return new TallyEndpointInfo(
                Host: normHost,
                Port: normPort,
                IsResponsive: false,
                FailureCause: ConnectionFailureCause.InvalidResponse,
                ErrorMessage: "Port is open but TallyPrime did not provide a valid XML response."
            );
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused })
        {
            return new TallyEndpointInfo(normHost, normPort, false, FailureCause: ConnectionFailureCause.ConnectionRefused, ErrorMessage: "Connection refused.");
        }
        catch (OperationCanceledException)
        {
            return new TallyEndpointInfo(normHost, normPort, false, FailureCause: ConnectionFailureCause.Timeout, ErrorMessage: "Connection timed out or cancelled.");
        }
        catch (Exception ex)
        {
            return new TallyEndpointInfo(normHost, normPort, false, FailureCause: ConnectionFailureCause.Unknown, ErrorMessage: ex.Message);
        }
    }

    public async Task<bool> TestConnectionAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var result = await TestConnectionDetailedAsync(host, port, cancellationToken);
        if (result.IsResponsive)
        {
            ActiveEndpoint = result;
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
