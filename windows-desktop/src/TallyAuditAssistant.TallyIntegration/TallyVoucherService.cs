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
        // Large financial years are deliberately read in small windows. A slow
        // TallyPrime/server connection should never require one huge XML response.
        // The window automatically shrinks when Tally cannot complete a request.
        const int minimumChunkDays = 1;
        const int maximumChunkDays = 7;
        const int maxAttemptsPerWindow = 3;

        var safeChunkDays = Math.Clamp(chunkDays, minimumChunkDays, maximumChunkDays);
        var currentStart = fromDate;

        while (currentStart <= toDate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remainingDays = (toDate - currentStart).Days + 1;
            var requestedChunkDays = Math.Min(safeChunkDays, remainingDays);
            var attempt = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var currentEnd = currentStart.AddDays(requestedChunkDays - 1);
                if (currentEnd > toDate)
                {
                    currentEnd = toDate;
                }

                _logger.LogInformation(
                    "Streaming voucher chunk from {Start:yyyy-MM-dd} to {End:yyyy-MM-dd} ({Days} days), attempt {Attempt}...",
                    currentStart,
                    currentEnd,
                    (currentEnd - currentStart).Days + 1,
                    attempt + 1);

                try
                {
                    var chunk = await GetVouchersAsync(
                        companyName,
                        currentStart,
                        currentEnd,
                        null,
                        cancellationToken);

                    foreach (var voucher in chunk)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return voucher;
                    }

                    // Once a smaller window succeeds, retain it for the rest of
                    // the sync. This prevents repeated timeouts on the same server.
                    if (requestedChunkDays < safeChunkDays)
                    {
                        safeChunkDays = requestedChunkDays;
                    }

                    currentStart = currentEnd.AddDays(1);
                    break;
                }
                catch (TallySynchronizationException ex) when (IsRecoverableChunkFailure(ex))
                {
                    attempt++;

                    if (attempt < maxAttemptsPerWindow)
                    {
                        var delaySeconds = attempt * 2;
                        _logger.LogWarning(
                            ex,
                            "Voucher chunk failed for {Start:yyyy-MM-dd} to {End:yyyy-MM-dd}. " +
                            "Retrying in {DelaySeconds}s (attempt {Attempt}/{MaxAttempts}).",
                            currentStart,
                            currentEnd,
                            delaySeconds,
                            attempt + 1,
                            maxAttemptsPerWindow);

                        EmitRetryLog(
                            companyName,
                            currentStart,
                            currentEnd,
                            delaySeconds,
                            requestedChunkDays,
                            ex);

                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                        continue;
                    }

                    // A repeated failure on a large window is handled by splitting
                    // the window rather than aborting the whole financial-year sync.
                    if (requestedChunkDays > minimumChunkDays)
                    {
                        var nextChunkDays = Math.Max(
                            minimumChunkDays,
                            requestedChunkDays / 2);

                        _logger.LogWarning(
                            ex,
                            "Voucher chunk repeatedly failed for {Start:yyyy-MM-dd} to {End:yyyy-MM-dd}. " +
                            "Reducing request window from {CurrentDays} to {NextDays} day(s).",
                            currentStart,
                            currentEnd,
                            requestedChunkDays,
                            nextChunkDays);

                        EmitTimeoutRecoveryLog(
                            companyName,
                            currentStart,
                            currentEnd,
                            nextChunkDays);

                        requestedChunkDays = nextChunkDays;
                        attempt = 0;
                        safeChunkDays = Math.Min(safeChunkDays, nextChunkDays);
                        continue;
                    }

                    // Even a one-day request is allowed a few retries, but after
                    // that the error is surfaced with the exact stage/company context.
                    throw;
                }
            }
        }
    }

    private static bool IsRecoverableChunkFailure(TallySynchronizationException ex)
    {
        // HTTP failures that commonly occur when a Tally/server node is busy,
        // overloaded, or temporarily unavailable.
        if (ex.HttpStatusCode is 408 or 429 or 500 or 502 or 503 or 504)
        {
            return true;
        }

        // A large XML response can be truncated/corrupted by an overloaded
        // Tally/server connection. Treat parse/empty-response failures as
        // recoverable so the request can be retried or split into smaller windows.
        return ex.IsEmptyResponse || ex.IsXmlParseFailure;
    }

    private void EmitRetryLog(
        string companyName,
        DateTime fromDate,
        DateTime toDate,
        int delaySeconds,
        int chunkDays,
        TallySynchronizationException exception)
    {
        _logger.LogInformation(
            "Adaptive voucher sync retry for {Company}: {Start:yyyy-MM-dd} to {End:yyyy-MM-dd}, " +
            "{ChunkDays}-day window after {DelaySeconds}s. Reason: {Reason}",
            companyName,
            fromDate,
            toDate,
            chunkDays,
            delaySeconds,
            exception.Message);
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
