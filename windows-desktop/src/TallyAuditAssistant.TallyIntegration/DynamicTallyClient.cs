using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.TallyIntegration.Mocks;

namespace TallyAuditAssistant.TallyIntegration;

public class DynamicTallyClient : ITallyClient
{
    private readonly TallyClient _realClient;
    private readonly MockTallyClient _mockClient;
    private readonly ISettingsService _settingsService;

    public DynamicTallyClient(TallyClient realClient, MockTallyClient mockClient, ISettingsService settingsService)
    {
        _realClient = realClient;
        _mockClient = mockClient;
        _settingsService = settingsService;
    }

    private async Task<ITallyClient> GetActiveClientAsync()
    {
        return await _settingsService.IsMockModeEnabledAsync() ? _mockClient : _realClient;
    }

    public async Task<string> PostXmlAsync(string endpointUrl, string xmlPayload, CancellationToken cancellationToken = default)
    {
        var client = await GetActiveClientAsync();
        return await client.PostXmlAsync(endpointUrl, xmlPayload, cancellationToken);
    }

    public async Task<TallyRawResponse> SendAsync(string endpointUrl, string payload, TallyRequestFormat format = TallyRequestFormat.Xml, CancellationToken cancellationToken = default)
    {
        var client = await GetActiveClientAsync();
        return await client.SendAsync(endpointUrl, payload, format, cancellationToken);
    }

    public async Task<bool> PingAsync(string endpointUrl, CancellationToken cancellationToken = default)
    {
        var client = await GetActiveClientAsync();
        return await client.PingAsync(endpointUrl, cancellationToken);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var client = await GetActiveClientAsync();
        return await client.TestConnectionAsync(cancellationToken);
    }

    public async Task<bool> TestConnectionAsync(string endpointUrl, CancellationToken cancellationToken = default)
    {
        var client = await GetActiveClientAsync();
        return await client.TestConnectionAsync(endpointUrl, cancellationToken);
    }
}
