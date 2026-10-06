using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Common;
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
        var (cleanHost, cleanPort, scheme) = TallyEndpointNormalization.Normalize(host, port <= 0 ? 9000 : port);
        return $"{scheme}://{cleanHost}:{cleanPort}";
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
            var msg = rawResponse.ErrorMessage ?? $"HTTP {rawResponse.HttpStatusCode}";
            _logger.LogError("Failed to query vouchers from Tally: {Error}", msg);
            throw new TallySynchronizationException(
                "READ VOUCHERS",
                companyName,
                url,
                $"Failed to read vouchers from TallyPrime: {msg}",
                rawResponse.HttpStatusCode,
                rawResponse.ErrorMessage,
                isEmptyResponse: string.IsNullOrWhiteSpace(rawResponse.Content));
        }

        try
        {
            var vouchers = _parser.ParseVouchers(rawResponse.Content, TallyRequestFormat.Xml);
            _logger.LogInformation("Successfully retrieved {Count} vouchers from TallyPrime", vouchers.Count);
            return vouchers;
        }
        catch (TallySynchronizationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse vouchers XML for {Company}", companyName);
            throw new TallySynchronizationException(
                "READ VOUCHERS",
                companyName,
                url,
                $"Failed to parse vouchers from TallyPrime: {ex.Message}",
                rawResponse.HttpStatusCode,
                ex.Message,
                isXmlParseFailure: true,
                innerException: ex);
        }
    }

    public async IAsyncEnumerable<TallyVoucherDto> StreamVouchersChunkedAsync(
        string companyName, 
        DateTime fromDate, 
        DateTime toDate, 
        int chunkDays = 7, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Server-hosted TallyPrime installations can be noticeably slower than a
        // local Tally instance. Start with a conservative 7-day window and
        // automatically reduce the window when Tally cannot produce the response
        // within the HTTP timeout. This keeps large FY requests from failing as
        // one monolithic voucher read.
        var safeChunkDays = Math.Clamp(chunkDays, 1, 30);
        var currentStart = fromDate;

        while (currentStart <= toDate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remainingDays = (toDate - currentStart).Days + 1;
            var requestedChunkDays = Math.Min(safeChunkDays, remainingDays);
            var chunkSucceeded = false;

            while (!chunkSucceeded)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var currentEnd = currentStart.AddDays(requestedChunkDays - 1);
                if (currentEnd > toDate) currentEnd = toDate;

                _logger.LogInformation(
                    "Streaming voucher chunk from {Start:yyyy-MM-dd} to {End:yyyy-MM-dd} ({Days} days)...",
                    currentStart, currentEnd, (currentEnd - currentStart).Days + 1);

                IReadOnlyList<TallyVoucherDto>? chunk = null;

                try
                {
                    chunk = await GetVouchersAsync(companyName, currentStart, currentEnd, null, cancellationToken);
                }
                catch (TallySynchronizationException ex) when (
                    ex.HttpStatusCode == 408 &&
                    requestedChunkDays > 1)
                {
                    var nextChunkDays = Math.Max(1, requestedChunkDays / 2);

                    _logger.LogWarning(
                        "Voucher request timed out for {Start:yyyy-MM-dd} to {End:yyyy-MM-dd}. Retrying with {NextDays}-day chunks.",
                        currentStart, currentEnd, nextChunkDays);

                    EmitTimeoutRecoveryLog(companyName, currentStart, currentEnd, nextChunkDays);
                    requestedChunkDays = nextChunkDays;
                    continue;
                }

                // Keep yield outside the try/catch. C# iterator methods cannot
                // yield a value inside a try block that has a catch clause.
                foreach (var voucher in chunk)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return voucher;
                }

                chunkSucceeded = true;

                // If a reduced chunk succeeded, keep using that smaller
                // size for the remainder of the sync. This avoids repeatedly
                // timing out against a slow Tally server.
                if (requestedChunkDays < safeChunkDays)
                {
                    safeChunkDays = requestedChunkDays;
                }

                currentStart = currentEnd.AddDays(1);
            }
        }
    }

    private void EmitTimeoutRecoveryLog(
        string companyName,
        DateTime fromDate,
        DateTime toDate,
        int nextChunkDays)
    {
        _logger.LogInformation(
            "Adaptive voucher sync recovery for {Company}: reducing request window to {Days} day(s).",
            companyName, nextChunkDays);
    }
}
