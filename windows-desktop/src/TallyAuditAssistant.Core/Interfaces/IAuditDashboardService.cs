using System;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IAuditDashboardService
{
    Task<AuditDashboardSummary> GetDashboardSummaryAsync(
        string companyId, 
        DateTime? periodFrom = null, 
        DateTime? periodTo = null, 
        CancellationToken cancellationToken = default);

    Task InvalidateCacheAsync(string? companyId = null);

    RiskScoringOptions Options { get; set; }
}
