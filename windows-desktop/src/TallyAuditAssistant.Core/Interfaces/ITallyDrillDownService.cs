using TallyAuditAssistant.Core.Domain.Tally;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ITallyDrillDownService
{
    Task<TallyVoucherDrillDownInfo?> GetVoucherDrillDownAsync(
        string companyId, 
        string voucherId, 
        CancellationToken cancellationToken = default);

    Task<TallyOpenAttemptResult> AttemptOpenInTallyAsync(
        string companyId, 
        string voucherId, 
        CancellationToken cancellationToken = default);

    TallyNavigationBreadcrumb GenerateNavigationGuide(
        string companyName,
        string voucherNumber,
        string voucherTypeName,
        DateTime voucherDate,
        string? masterId = null);
}
