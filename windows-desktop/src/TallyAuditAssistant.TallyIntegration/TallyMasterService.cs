using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Common;
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
        var (cleanHost, cleanPort, scheme) = TallyEndpointNormalization.Normalize(host, port <= 0 ? 9000 : port);
        return $"{scheme}://{cleanHost}:{cleanPort}";
    }

    public async Task<IReadOnlyList<TallyLedgerDto>> GetLedgersAsync(string companyName, long? fromAlterId = null, CancellationToken cancellationToken = default)
    {
        var url = await GetEndpointAsync();
        var requestXml = _requestBuilder.BuildLedgerCollectionRequest(companyName, fromAlterId, TallyRequestFormat.Xml);

        _logger.LogInformation("Querying ledgers for company {Company} (fromAlterId: {AlterId})...", companyName, fromAlterId);
        var rawResponse = await _client.SendAsync(url, requestXml, TallyRequestFormat.Xml, cancellationToken);
        if (!rawResponse.IsSuccess)
        {
            var msg = rawResponse.ErrorMessage ?? $"HTTP {rawResponse.HttpStatusCode}";
            _logger.LogError("Failed to query ledgers from Tally: {Error}", msg);
            throw new TallySynchronizationException(
                "READ LEDGERS",
                companyName,
                url,
                $"Failed to read ledgers from TallyPrime: {msg}",
                rawResponse.HttpStatusCode,
                rawResponse.ErrorMessage,
                isEmptyResponse: string.IsNullOrWhiteSpace(rawResponse.Content));
        }

        try
        {
            var ledgers = _parser.ParseLedgers(rawResponse.Content, TallyRequestFormat.Xml);
            _logger.LogInformation("Successfully parsed {Count} ledgers for {Company}", ledgers.Count, companyName);
            return ledgers;
        }
        catch (TallySynchronizationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse ledgers XML for {Company}", companyName);
            throw new TallySynchronizationException(
                "READ LEDGERS",
                companyName,
                url,
                $"Failed to parse ledgers from TallyPrime: {ex.Message}",
                rawResponse.HttpStatusCode,
                ex.Message,
                isXmlParseFailure: true,
                innerException: ex);
        }
    }

    public async Task<IReadOnlyList<string>> GetGroupsAsync(string companyName, CancellationToken cancellationToken = default)
    {
        var url = await GetEndpointAsync();
        var requestXml = _requestBuilder.BuildGroupCollectionRequest(companyName, TallyRequestFormat.Xml);

        _logger.LogInformation("Querying groups for company {Company}...", companyName);
        var rawResponse = await _client.SendAsync(url, requestXml, TallyRequestFormat.Xml, cancellationToken);
        if (!rawResponse.IsSuccess)
        {
            var msg = rawResponse.ErrorMessage ?? $"HTTP {rawResponse.HttpStatusCode}";
            _logger.LogError("Failed to query groups from Tally: {Error}", msg);
            throw new TallySynchronizationException(
                "READ GROUPS",
                companyName,
                url,
                $"Failed to read groups from TallyPrime: {msg}",
                rawResponse.HttpStatusCode,
                rawResponse.ErrorMessage,
                isEmptyResponse: string.IsNullOrWhiteSpace(rawResponse.Content));
        }

        try
        {
            var groups = _parser.ParseGroups(rawResponse.Content, TallyRequestFormat.Xml);
            _logger.LogInformation("Successfully parsed {Count} groups for {Company}", groups.Count, companyName);
            return groups;
        }
        catch (TallySynchronizationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse groups XML for {Company}", companyName);
            throw new TallySynchronizationException(
                "READ GROUPS",
                companyName,
                url,
                $"Failed to parse groups from TallyPrime: {ex.Message}",
                rawResponse.HttpStatusCode,
                ex.Message,
                isXmlParseFailure: true,
                innerException: ex);
        }
    }
}
