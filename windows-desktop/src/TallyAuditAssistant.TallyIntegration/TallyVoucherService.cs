using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.TallyIntegration;

public class TallyVoucherService : ITallyVoucherService
{
    private readonly ITallyClient _client;
    private readonly ITallyRequestBuilder _requestBuilder;
    private readonly ITallyResponseParser _parser;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TallyVoucherService> _logger;

    public TallyVoucherService(
        ITallyClient client,
        ITallyRequestBuilder requestBuilder,
        ITallyResponseParser parser,
        ISettingsService settingsService,
        ILogger<TallyVoucherService> logger)
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

    public async Task<IReadOnlyList<TallyVoucherDto>> GetVouchersAsync(
        string companyName, 
        DateTime fromDate, 
        DateTime toDate, 
        long? fromAlterId = null, 
        CancellationToken cancellationToken = default)
    {
        var url = await GetEndpointAsync();
        var requestXml = _requestBuilder.BuildVoucherCollectionRequest(companyName, fromDate, toDate, fromAlterId, TallyRequestFormat.Xml);

        _logger.LogInformation("Querying vouchers for {Company} between {From:yyyy-MM-dd} and {To:yyyy-MM-dd}...", companyName, fromDate, toDate);
        var rawResponse = await _client.SendAsync(url, requestXml, TallyRequestFormat.Xml, cancellationToken);
        if (!rawResponse.IsSuccess)
        {
            _logger.LogError("Failed to query vouchers from Tally: {Error}", rawResponse.ErrorMessage);
            return Array.Empty<TallyVoucherDto>();
        }

        var vouchers = _parser.ParseVouchers(rawResponse.Content, TallyRequestFormat.Xml);
        _logger.LogInformation("Successfully retrieved {Count} vouchers from TallyPrime", vouchers.Count);
        return vouchers;
    }

    public async IAsyncEnumerable<TallyVoucherDto> StreamVouchersChunkedAsync(
        string companyName, 
        DateTime fromDate, 
        DateTime toDate, 
        int chunkDays = 30, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var currentStart = fromDate;

        while (currentStart <= toDate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentEnd = currentStart.AddDays(chunkDays - 1);
            if (currentEnd > toDate) currentEnd = toDate;

            _logger.LogInformation("Streaming voucher chunk from {Start:yyyy-MM-dd} to {End:yyyy-MM-dd}...", currentStart, currentEnd);

            var chunk = await GetVouchersAsync(companyName, currentStart, currentEnd, null, cancellationToken);
            foreach (var voucher in chunk)
            {
                yield return voucher;
            }

            currentStart = currentEnd.AddDays(1);
        }
    }
}
