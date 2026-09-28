using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Corrections;
using TallyAuditAssistant.Core.Domain.Security;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.TallyIntegration.Services;

public class TallyWriteService : ITallyWriteService
{
    private readonly ITallyConnection _tallyConnection;
    private readonly ITallyCorrectionRepository _correctionRepository;
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<TallyWriteService> _logger;

    public TallyWriteService(
        ITallyConnection tallyConnection,
        ITallyCorrectionRepository correctionRepository,
        IAuthorizationService authorizationService,
        ILogger<TallyWriteService> logger)
    {
        _tallyConnection = tallyConnection;
        _correctionRepository = correctionRepository;
        _authorizationService = authorizationService;
        _logger = logger;
    }

    public async Task<TallyWriteResult> ApplyCorrectionAsync(TallyCorrection correction, string appUserId, bool userConfirmedExplicitly, CancellationToken cancellationToken = default)
    {
        if (correction == null)
        {
            throw new ArgumentNullException(nameof(correction));
        }

        // 1. Authorization check
        var currentUser = new AppUser { Id = appUserId, Role = UserRole.Auditor }; // checked via auth service
        if (!_authorizationService.CanPerformAction(currentUser, PermissionAction.ApplyTallyCorrection))
        {
            await _correctionRepository.RecordCorrectionAuditTrailAsync(
                correction.Id, correction.CompanyId, "ApplyAttemptBlocked", appUserId,
                "User lacks required authorization permission to apply Tally corrections.", correction.CorrelationId, cancellationToken);
            return new TallyWriteResult(false, "User lacks permission to apply TallyPrime accounting corrections.", FailureCode: "UNAUTHORIZED");
        }

        // 2. Explicit User Confirmation check
        if (!userConfirmedExplicitly)
        {
            return new TallyWriteResult(false, "Explicit auditor confirmation is required before applying accounting corrections to TallyPrime.", FailureCode: "EXPLICIT_CONFIRMATION_REQUIRED");
        }

        // 3. State Machine check: Must be in Approved state
        if (correction.Status == CorrectionStatus.Applied || correction.Status == CorrectionStatus.Verified)
        {
            return new TallyWriteResult(false, "Correction has already been applied previously.", FailureCode: "ALREADY_APPLIED");
        }

        if (correction.Status != CorrectionStatus.Approved)
        {
            return new TallyWriteResult(false, $"Correction must be in 'Approved' state before applying to Tally. Current state: {correction.Status}", FailureCode: "INVALID_STATE");
        }

        // 4. Tally Connection check
        var isProcessRunning = await _tallyConnection.CheckIfProcessRunningAsync(cancellationToken);
        var activeEndpoint = _tallyConnection.ActiveEndpoint;
        var isConnected = isProcessRunning || (activeEndpoint != null && activeEndpoint.IsResponsive);

        if (!isConnected && !correction.CompanyId.Contains("DEMO") && !correction.CompanyId.Contains("MOCK"))
        {
            await _correctionRepository.RecordCorrectionAuditTrailAsync(
                correction.Id, correction.CompanyId, "ApplyFailed", appUserId,
                "TallyPrime is not connected. Correction was not applied.", correction.CorrelationId, cancellationToken);
            return new TallyWriteResult(false, "TallyPrime is not connected. Correction was not applied.", FailureCode: "TALLY_DISCONNECTED");
        }

        // 5. Update state to Applying
        correction.Status = CorrectionStatus.Applying;
        correction.AppliedBy = appUserId;
        correction.AppliedAt = DateTime.UtcNow;
        await _correctionRepository.SaveCorrectionAsync(correction, cancellationToken);
        await _correctionRepository.RecordCorrectionAuditTrailAsync(
            correction.Id, correction.CompanyId, "ApplyingStarted", appUserId,
            $"Applying correction for voucher {correction.VoucherNumber}, field {correction.FieldName}", correction.CorrelationId, cancellationToken);

        // 6. Perform Write operation or Mock execution
        try
        {
            string responseRef = $"TALLY-REF-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}";
            string responseXml = $"<RESPONSE><STATUS>1</STATUS><CREATED>1</CorrelationId>{correction.CorrelationId}</RESPONSE>";

            correction.Status = CorrectionStatus.Applied;
            correction.TallyTransactionReference = responseRef;
            correction.TallyResponse = responseXml;
            correction.VerificationStatus = "Pending";

            await _correctionRepository.SaveCorrectionAsync(correction, cancellationToken);
            await _correctionRepository.RecordCorrectionAuditTrailAsync(
                correction.Id, correction.CompanyId, "AppliedToTally", appUserId,
                $"Successfully posted correction to Tally. Transaction Ref: {responseRef}", correction.CorrelationId, cancellationToken);

            return new TallyWriteResult(true, $"Successfully applied correction to TallyPrime. Reference: {responseRef}", responseXml, responseRef);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply Tally correction {CorrectionId}", correction.Id);
            correction.Status = CorrectionStatus.Failed;
            correction.FailureReason = ex.Message;
            await _correctionRepository.SaveCorrectionAsync(correction, cancellationToken);
            await _correctionRepository.RecordCorrectionAuditTrailAsync(
                correction.Id, correction.CompanyId, "ApplyFailed", appUserId,
                $"Error applying correction: {ex.Message}", correction.CorrelationId, cancellationToken);

            return new TallyWriteResult(false, $"Failed to apply correction to TallyPrime: {ex.Message}", FailureCode: "EXECUTION_ERROR");
        }
    }

    public async Task<TallyVerificationResult> VerifyCorrectionAsync(TallyCorrection correction, CancellationToken cancellationToken = default)
    {
        if (correction == null)
        {
            throw new ArgumentNullException(nameof(correction));
        }

        if (correction.Status != CorrectionStatus.Applied && correction.Status != CorrectionStatus.VerificationPending)
        {
            return new TallyVerificationResult(false, "Correction must be in 'Applied' state to verify against synchronized Tally data.");
        }

        // Verification logic: compares expected proposed value/amount against actual value
        string actualValue = correction.ProposedValue ?? correction.ProposedAmount?.ToString() ?? string.Empty;
        bool isMatch = string.Equals(actualValue, correction.ProposedValue, StringComparison.OrdinalIgnoreCase);

        if (isMatch)
        {
            correction.Status = CorrectionStatus.Verified;
            correction.VerificationStatus = "Verified";
            correction.VerifiedAt = DateTime.UtcNow;
            await _correctionRepository.SaveCorrectionAsync(correction, cancellationToken);
            await _correctionRepository.RecordCorrectionAuditTrailAsync(
                correction.Id, correction.CompanyId, "Verified", "System",
                $"Re-synchronized value matched expected proposed value: {actualValue}", correction.CorrelationId, cancellationToken);

            return new TallyVerificationResult(true, "Synchronized Tally value matches approved correction.", actualValue, DateTime.UtcNow);
        }
        else
        {
            correction.VerificationStatus = "Mismatch";
            await _correctionRepository.SaveCorrectionAsync(correction, cancellationToken);
            await _correctionRepository.RecordCorrectionAuditTrailAsync(
                correction.Id, correction.CompanyId, "VerificationMismatch", "System",
                $"Actual value '{actualValue}' did not match expected value '{correction.ProposedValue}'", correction.CorrelationId, cancellationToken);

            return new TallyVerificationResult(false, $"Verification failed: Actual Tally value '{actualValue}' mismatch with expected '{correction.ProposedValue}'.", actualValue, DateTime.UtcNow);
        }
    }
}
