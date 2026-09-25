using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

public record TallyEndpointInfo(
    string Host,
    int Port,
    bool IsResponsive,
    string? ServerVersion = null,
    string? ActiveCompany = null,
    long LatencyMs = 0);

public interface ITallyConnection
{
    ConnectionStatus CurrentStatus { get; }
    TallyEndpointInfo? ActiveEndpoint { get; }
    string? LastErrorMessage { get; }

    Task<bool> CheckIfProcessRunningAsync(CancellationToken cancellationToken = default);
    Task<TallyEndpointInfo?> ProbePortRangeAsync(string host = "localhost", int startPort = 9000, int endPort = 9005, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(string host, int port, CancellationToken cancellationToken = default);
    
    event EventHandler<ConnectionStatus>? StatusChanged;
}
