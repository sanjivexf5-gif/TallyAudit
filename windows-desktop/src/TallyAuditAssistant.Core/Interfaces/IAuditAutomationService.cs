using TallyAuditAssistant.Core.Domain.Audit;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IAuditAutomationService
{
    AuditAutomationStage CurrentStage { get; }
    event EventHandler<AuditAutomationProgress>? ProgressChanged;

    Task<AuditAutomationResult> RunAsync(
        bool runIncrementalSync = true,
        bool runFullAudit = true,
        CancellationToken cancellationToken = default);

    Task CancelAsync();
}
