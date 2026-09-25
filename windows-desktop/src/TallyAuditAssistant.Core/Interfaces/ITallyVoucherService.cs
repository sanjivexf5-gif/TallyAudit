using TallyAuditAssistant.Core.Domain.Tally;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ITallyVoucherService
{
    Task<IReadOnlyList<TallyVoucherDto>> GetVouchersAsync(
        string companyName, 
        DateTime fromDate, 
        DateTime toDate, 
        long? fromAlterId = null, 
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<TallyVoucherDto> StreamVouchersChunkedAsync(
        string companyName, 
        DateTime fromDate, 
        DateTime toDate, 
        int chunkDays = 30, 
        CancellationToken cancellationToken = default);
}
