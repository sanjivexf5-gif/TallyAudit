using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IReconciliationEngine
{
    IReadOnlyList<IReconciliationRule> RegisteredRules { get; }
    Task<IReadOnlyList<AuditResult>> ExecuteReconciliationsAsync(AuditExecutionContext context, CancellationToken cancellationToken = default);
}
