using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.TallyIntegration;

public class TallyMasterService : ITallyMasterService
{
    private readonly ITallyClient _client;
    private readonly ITallyRequestBuilder _requestBuilder;
    private readonly ITallyResponseParser _parser;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TallyMasterService> _logger;

    public TallyMasterService(
        ITallyClient client,
        ITallyRequestBuilder requestBuilder,
        ITallyResponseParser parser,
        ISettingsService settingsService,
        ILogger<TallyMasterService> logger)
    {
        _client = client;
        _requestBuilder = requestBuilder;
        _parser = parser;
        _settingsService = settingsService;
        _logger = logger;
    }

    private async Task<string> GetEndpointAsync()
    {
        var host = await _settingsService.GetTallyHostAsync();
        var port = await _settingsService.GetTallyPortAsync();
        return $"http://{host}:{port}";
    }

    public async Task<IReadOnlyList<TallyLedgerDto>> GetLedgersAsync(string companyName, long? fromAlterId = null, CancellationToken cancellationToken = default)
    {
        var url = await GetEndpointAsync();
        var requestXml = _requestBuilder.BuildLedgerCollectionRequest(companyName, fromAlterId, TallyRequestFormat.Xml);

        _logger.LogInformation("Querying ledgers for company {Company} (fromAlterId: {AlterId})...", companyName, fromAlterId);
        var rawResponse = await _client.SendAsync(url, requestXml, TallyRequestFormat.Xml, cancellationToken);
        if (!rawResponse.IsSuccess)
        {
            _logger.LogError("Failed to query ledgers from Tally: {Error}", rawResponse.ErrorMessage);
            return Array.Empty<TallyLedgerDto>();
        }

        var ledgers = _parser.ParseLedgers(rawResponse.Content, TallyRequestFormat.Xml);
        _logger.LogInformation("Successfully parsed {Count} ledgers for {Company}", ledgers.Count, companyName);
        return ledgers;
    }

    public async Task<IReadOnlyList<string>> GetGroupsAsync(string companyName, CancellationToken cancellationToken = default)
    {
        var url = await GetEndpointAsync();
        var requestXml = _requestBuilder.BuildGroupCollectionRequest(companyName, TallyRequestFormat.Xml);

        var rawResponse = await _client.SendAsync(url, requestXml, TallyRequestFormat.Xml, cancellationToken);
        if (!rawResponse.IsSuccess)
        {
            _logger.LogError("Failed to query groups from Tally: {Error}", rawResponse.ErrorMessage);
            return Array.Empty<string>();
        }

        return _parser.ParseGroups(rawResponse.Content, TallyRequestFormat.Xml);
    }
}
