using Microsoft.Extensions.Logging;
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
        if (!string.IsNullOrEmpty(endpointUrl)) return endpointUrl;

        var host = await _settingsService.GetTallyHostAsync();
        var port = await _settingsService.GetTallyPortAsync();
        return $"http://{host}:{port}";
    }

    public async Task<IReadOnlyList<string>> GetOpenCompaniesAsync(string? endpointUrl = null, CancellationToken cancellationToken = default)
    {
        var url = await ResolveEndpointAsync(endpointUrl);
        var requestXml = _requestBuilder.BuildCompanyListRequest(TallyRequestFormat.Xml);

        var rawResponse = await _client.SendAsync(url, requestXml, TallyRequestFormat.Xml, cancellationToken);
        if (!rawResponse.IsSuccess)
        {
            _logger.LogWarning("Failed to query open company list from {Url}: {Error}", url, rawResponse.ErrorMessage);
            return Array.Empty<string>();
        }

        return _parser.ParseCompanyList(rawResponse.Content, TallyRequestFormat.Xml);
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
            _logger.LogWarning("Failed to retrieve company profile for {Company}: {Error}", companyName, rawResponse.ErrorMessage);
            return null;
        }

        return _parser.ParseCompanyProfile(rawResponse.Content, TallyRequestFormat.Xml);
    }
}
