using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

public enum ConnectionFailureCause
{
    None = 0,
    NotRunning = 1,
    ConnectionRefused = 2,
    Timeout = 3,
    InvalidResponse = 4,
    FirewallBlocked = 5,
    NoCompanyLoaded = 6,
    Unknown = 99
}

public record TallyEndpointInfo(
    string Host,
    int Port,
    bool IsResponsive,
    string? ServerVersion = null,
    string? ActiveCompany = null,
    long LatencyMs = 0,
    string? ErrorMessage = null,
    ConnectionFailureCause FailureCause = ConnectionFailureCause.None);

public record TallyDiagnosticReport(
    bool HostResolved,
    bool PortReachable,
    bool TallyResponseReceived,
    bool ValidTallyResponse,
    bool CompanyDetected,
    string? Summary);

public interface ITallyConnection
{
    ConnectionStatus CurrentStatus { get; }
    TallyEndpointInfo? ActiveEndpoint { get; }
    string? LastErrorMessage { get; }

    Task<bool> CheckIfProcessRunningAsync(CancellationToken cancellationToken = default);
    Task<TallyEndpointInfo?> ProbePortRangeAsync(string host = "localhost", int startPort = 9000, int endPort = 9005, CancellationToken cancellationToken = default);
    Task<TallyEndpointInfo?> DiscoverTallyAsync(string? preferredHost = null, int? preferredPort = null, int scanRangeMax = 9005, CancellationToken cancellationToken = default);
    Task<TallyEndpointInfo> TestConnectionDetailedAsync(string host, int port, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(string host, int port, CancellationToken cancellationToken = default);
    
    event EventHandler<ConnectionStatus>? StatusChanged;
}
