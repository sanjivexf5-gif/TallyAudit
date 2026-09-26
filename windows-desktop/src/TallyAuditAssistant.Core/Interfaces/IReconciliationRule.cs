using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IReconciliationRule
{
    string RuleId { get; }
    string RuleCode { get; }
    string RuleName { get; }
    RuleCategory Category { get; }
    string Description { get; }
    bool IsEnabled { get; set; }
    Task<IReadOnlyList<AuditResult>> ExecuteAsync(AuditExecutionContext context, CancellationToken cancellationToken = default);
}
