using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Common;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.TallyIntegration;

public class TallyCompanyService : ITallyCompanyService
{
    private readonly ITallyClient _client;
    private readonly ITallyRequestBuilder _requestBuilder;
    private readonly ITallyResponseParser _parser;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TallyCompanyService> _logger;

    public TallyCompanyService(
        ITallyClient client,
        ITallyRequestBuilder requestBuilder,
        ITallyResponseParser parser,
        ISettingsService settingsService,
        ILogger<TallyCompanyService> logger)
    {
        _client = client;
        _requestBuilder = requestBuilder;
        _parser = parser;
        _settingsService = settingsService;
        _logger = logger;
    }

    private async Task<string> ResolveEndpointAsync(string? endpointUrl)
    {
        if (!string.IsNullOrEmpty(endpointUrl))
        {
            var (h, p, s) = TallyEndpointNormalization.Normalize(endpointUrl, 9000);
            return $"{s}://{h}:{p}";
        }

        var host = await _settingsService.GetTallyHostAsync();
        var port = await _settingsService.GetTallyPortAsync();
        var (cleanHost, cleanPort, scheme) = TallyEndpointNormalization.Normalize(host, port <= 0 ? 9000 : port);
        return $"{scheme}://{cleanHost}:{cleanPort}";
    }

    public async Task<IReadOnlyList<string>> GetOpenCompaniesAsync(string? endpointUrl = null, CancellationToken cancellationToken = default)
    {
        var url = await ResolveEndpointAsync(endpointUrl);
        var requestXml = _requestBuilder.BuildCompanyListRequest(TallyRequestFormat.Xml);

        var rawResponse = await _client.SendAsync(url, requestXml, TallyRequestFormat.Xml, cancellationToken);
        if (!rawResponse.IsSuccess)
        {
            var msg = rawResponse.ErrorMessage ?? $"HTTP {rawResponse.HttpStatusCode}";
            _logger.LogWarning("Failed to query open company list from {Url}: {Error}", url, msg);
            throw new TallySynchronizationException(
                "CompanyDiscovery",
                string.Empty,
                url,
                $"Failed to query open company list from TallyPrime at {url}: {msg}",
                rawResponse.HttpStatusCode,
                rawResponse.ErrorMessage,
                isEmptyResponse: string.IsNullOrWhiteSpace(rawResponse.Content));
        }

        try
        {
            return _parser.ParseCompanyList(rawResponse.Content, TallyRequestFormat.Xml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse open company list from TallyPrime at {Url}", url);
            throw new TallySynchronizationException(
                "CompanyDiscovery",
                string.Empty,
                url,
                $"Failed to parse open company list from TallyPrime: {ex.Message}",
                rawResponse.HttpStatusCode,
                ex.Message,
                isXmlParseFailure: true,
                innerException: ex);
        }
    }

    public async Task<string?> GetActiveCompanyAsync(string? endpointUrl = null, CancellationToken cancellationToken = default)
    {
        var companies = await GetOpenCompaniesAsync(endpointUrl, cancellationToken);
        return companies.Count > 0 ? companies[0] : null;
    }

    public async Task<Dictionary<string, string>> GetCompanyProfileAsync(string endpointUrl, string companyName, CancellationToken cancellationToken = default)
    {
        var profile = await GetCompanyProfileTypedAsync(companyName, endpointUrl, cancellationToken);
        var dict = new Dictionary<string, string>();
        if (profile != null)
        {
            dict["Name"] = profile.Name;
            dict["FormalName"] = profile.FormalName ?? profile.Name;
            dict["GSTIN"] = profile.GSTIN ?? string.Empty;
            dict["PAN"] = profile.PAN ?? string.Empty;
            dict["State"] = profile.StateName ?? string.Empty;
            dict["BooksFrom"] = profile.BooksBeginningFrom.ToString("yyyy-MM-dd");
        }
        else
        {
            dict["Name"] = companyName;
        }
        return dict;
    }

    public async Task<TallyCompanyProfile?> GetCompanyProfileTypedAsync(string companyName, string? endpointUrl = null, CancellationToken cancellationToken = default)
    {
        var url = await ResolveEndpointAsync(endpointUrl);
        var requestXml = _requestBuilder.BuildCompanyProfileRequest(companyName, TallyRequestFormat.Xml);

        var rawResponse = await _client.SendAsync(url, requestXml, TallyRequestFormat.Xml, cancellationToken);
        if (!rawResponse.IsSuccess)
        {
            var msg = rawResponse.ErrorMessage ?? $"HTTP {rawResponse.HttpStatusCode}";
            _logger.LogWarning("Failed to retrieve company profile for {Company}: {Error}", companyName, msg);
            throw new TallySynchronizationException(
                "SELECT COMPANY",
                companyName,
                url,
                $"Failed to read company profile from TallyPrime for '{companyName}': {msg}",
                rawResponse.HttpStatusCode,
                rawResponse.ErrorMessage,
                isEmptyResponse: string.IsNullOrWhiteSpace(rawResponse.Content));
        }

        try
        {
            return _parser.ParseCompanyProfile(rawResponse.Content, TallyRequestFormat.Xml);
        }
        catch (TallySynchronizationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse company profile XML for {Company}", companyName);
            throw new TallySynchronizationException(
                "SELECT COMPANY",
                companyName,
                url,
                $"Failed to parse company profile from TallyPrime for '{companyName}': {ex.Message}",
                rawResponse.HttpStatusCode,
                ex.Message,
                isXmlParseFailure: true,
                innerException: ex);
        }
    }
}
