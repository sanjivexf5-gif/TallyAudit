using TallyAuditAssistant.Core.Domain.Diagnostics;

namespace TallyAuditAssistant.Core.Interfaces;

public interface IDiagnosticsService
{
    Task<SystemDiagnosticsReport> RunDiagnosticsAsync(CancellationToken ct = default);
}
