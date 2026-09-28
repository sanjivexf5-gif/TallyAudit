using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Corrections;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ITallyWriteService
{
    Task<TallyWriteResult> ApplyCorrectionAsync(TallyCorrection correction, string appUserId, bool userConfirmedExplicitly, CancellationToken cancellationToken = default);
    Task<TallyVerificationResult> VerifyCorrectionAsync(TallyCorrection correction, CancellationToken cancellationToken = default);
}
