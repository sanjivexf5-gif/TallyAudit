using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IAuditAssistantService
{
    bool IsAiEnabled { get; }
    string CurrentProviderName { get; }
    Task<string> ExplainFindingAsync(AuditException exception, CancellationToken cancellationToken = default);
    Task<string> SuggestReviewQuestionsAsync(AuditException exception, CancellationToken cancellationToken = default);
    Task<string> DraftWorkingPaperRemarkAsync(AuditException exception, CancellationToken cancellationToken = default);
    Task<string> ExplainReconciliationAsync(AuditException exception, CancellationToken cancellationToken = default);
    Task<string> SummarizeAuditRunAsync(int transactionsAudited, int rulesExecuted, int findings, int highPriority, int reviewRequired, string additionalStatsJson, CancellationToken cancellationToken = default);
}
