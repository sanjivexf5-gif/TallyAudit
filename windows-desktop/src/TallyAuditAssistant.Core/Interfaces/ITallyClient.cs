using TallyAuditAssistant.Core.Domain.Tally;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ITallyClient
{
    Task<string> PostXmlAsync(string endpointUrl, string xmlPayload, CancellationToken cancellationToken = default);
    Task<TallyRawResponse> SendAsync(string endpointUrl, string payload, TallyRequestFormat format = TallyRequestFormat.Xml, CancellationToken cancellationToken = default);
    Task<bool> PingAsync(string endpointUrl, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(string endpointUrl, CancellationToken cancellationToken = default);
}
