using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.TallyIntegration;

public class TallyClient : ITallyClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TallyClient> _logger;
    private readonly ITallyResponseParser? _parser;

    public TallyClient(HttpClient httpClient, ILogger<TallyClient> logger, ITallyResponseParser? parser = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _parser = parser;
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<string> PostXmlAsync(string endpointUrl, string xmlPayload, CancellationToken cancellationToken = default)
    {
        var rawResponse = await SendAsync(endpointUrl, xmlPayload, TallyRequestFormat.Xml, cancellationToken);
        if (!rawResponse.IsSuccess)
        {
            throw new InvalidOperationException(rawResponse.ErrorMessage ?? "Failed to communicate with Tally.");
        }
        return rawResponse.Content;
    }

    public async Task<TallyRawResponse> SendAsync(string endpointUrl, string payload, TallyRequestFormat format = TallyRequestFormat.Xml, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var mediaType = format == TallyRequestFormat.Json ? "application/json" : "text/xml";

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, mediaType);
            using var response = await _httpClient.PostAsync(endpointUrl, content, cancellationToken);
            sw.Stop();

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Tally endpoint {Url} returned HTTP {StatusCode}", endpointUrl, (int)response.StatusCode);
                return new TallyRawResponse(
                    IsSuccess: false,
                    HttpStatusCode: (int)response.StatusCode,
                    Content: responseBody,
                    LatencyMs: sw.ElapsedMilliseconds,
                    ErrorMessage: $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            // Check if response contains internal Tally errors
            string? tallyError = null;
            if (_parser != null)
            {
                var (hasError, errMsg) = _parser.CheckForTallyErrors(responseBody, format);
                if (hasError)
                {
                    tallyError = errMsg;
                    _logger.LogWarning("Tally reported internal error: {Error}", errMsg);
                }
            }

            return new TallyRawResponse(
                IsSuccess: string.IsNullOrEmpty(tallyError),
                HttpStatusCode: 200,
                Content: responseBody,
                LatencyMs: sw.ElapsedMilliseconds,
                ErrorMessage: tallyError);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Tally connection failed at {Url}: {Message}", endpointUrl, ex.Message);
            return new TallyRawResponse(
                IsSuccess: false,
                HttpStatusCode: 0,
                Content: string.Empty,
                LatencyMs: sw.ElapsedMilliseconds,
                ErrorMessage: $"Cannot connect to TallyPrime at {endpointUrl}. Ensure Tally is running and port is open.");
        }
        catch (TaskCanceledException ex)
        {
            sw.Stop();
            _logger.LogError("Tally request timed out after {Elapsed}ms at {Url}", sw.ElapsedMilliseconds, endpointUrl);
            return new TallyRawResponse(
                IsSuccess: false,
                HttpStatusCode: 408,
                Content: string.Empty,
                LatencyMs: sw.ElapsedMilliseconds,
                ErrorMessage: $"Request to TallyPrime timed out after {sw.ElapsedMilliseconds}ms.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Unexpected error communicating with Tally at {Url}", endpointUrl);
            return new TallyRawResponse(
                IsSuccess: false,
                HttpStatusCode: 500,
                Content: string.Empty,
                LatencyMs: sw.ElapsedMilliseconds,
                ErrorMessage: ex.Message);
        }
    }

    public async Task<bool> PingAsync(string endpointUrl, CancellationToken cancellationToken = default)
    {
        const string pingEnvelope = @"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Data</TYPE>
    <ID>System Information</ID>
  </HEADER>
  <BODY><DESC></DESC></BODY>
</ENVELOPE>";

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            using var content = new StringContent(pingEnvelope, Encoding.UTF8, "text/xml");
            var response = await _httpClient.PostAsync(endpointUrl, content, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
