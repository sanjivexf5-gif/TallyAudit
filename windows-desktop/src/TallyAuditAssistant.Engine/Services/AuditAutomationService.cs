using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Sync;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Engine.Services;

/// <summary>
/// Orchestrates repetitive audit preparation steps into one safe workflow.
/// TallyPrime remains strictly read-only; analysis and results are stored locally.
/// </summary>
public sealed class AuditAutomationService : IAuditAutomationService
{
    private readonly ITallyConnection _tallyConnection;
    private readonly IActiveCompanyContext _companyContext;
    private readonly ISyncManager _syncManager;
    private readonly IAuditEngine _auditEngine;
    private readonly IAuditTrailService? _auditTrailService;
    private readonly ILogger<AuditAutomationService> _logger;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private CancellationTokenSource? _runCts;

    public AuditAutomationStage CurrentStage { get; private set; } = AuditAutomationStage.Idle;
    public event EventHandler<AuditAutomationProgress>? ProgressChanged;

    public AuditAutomationService(
        ITallyConnection tallyConnection,
        IActiveCompanyContext companyContext,
        ISyncManager syncManager,
        IAuditEngine auditEngine,
        ILogger<AuditAutomationService> logger,
        IAuditTrailService? auditTrailService = null)
    {
        _tallyConnection = tallyConnection;
        _companyContext = companyContext;
        _syncManager = syncManager;
        _auditEngine = auditEngine;
        _logger = logger;
        _auditTrailService = auditTrailService;
    }

    public async Task<AuditAutomationResult> RunAsync(
        bool runIncrementalSync = true,
        bool runFullAudit = true,
        CancellationToken cancellationToken = default)
    {
        await _runGate.WaitAsync(cancellationToken);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _runCts?.Dispose();
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _runCts.Token;

        try
        {
            SetProgress(AuditAutomationStage.Connecting, "CONNECTING",
                "Automatically finding and verifying the TallyPrime HTTP server...", 5);

            var endpoint = _tallyConnection.ActiveEndpoint;
            if (endpoint?.IsResponsive != true)
            {
                endpoint = await _tallyConnection.DiscoverTallyAsync(
                    preferredHost: "localhost",
                    preferredPort: 9000,
                    scanRangeMax: 9100,
                    cancellationToken: ct);
            }

            if (endpoint?.IsResponsive != true)
            {
                throw new InvalidOperationException(
                    "TallyPrime could not be reached automatically. Open TallyPrime and make sure its HTTP server is enabled.");
            }

            SetProgress(AuditAutomationStage.SelectingCompany, "SELECTING COMPANY",
                "Resolving the active TallyPrime company and audit period...", 12);

            var company = await _companyContext.EnsureAndInitializeActiveCompanyAsync(ct);
            if (company == null || string.IsNullOrWhiteSpace(company.TallyCompanyName))
            {
                throw new InvalidOperationException(
                    "No active TallyPrime company was available. Open the company in TallyPrime and retry.");
            }

            await RecordAsync(
                "Automated audit workflow started",
                $"Automation started for '{company.TallyCompanyName}'.");

            if (runIncrementalSync)
            {
                SetProgress(AuditAutomationStage.Synchronizing, "SYNCHRONIZING",
                    $"Synchronizing latest TallyPrime data for '{company.TallyCompanyName}'...", 25);

                var syncAttempt = 1;
                var syncResult = await _syncManager.StartSyncAsync(
                    company.TallyCompanyName,
                    SyncMode.Incremental,
                    ct);

                // Restart only when the failure is a transient connection-stage error.
                // This stage runs before any company, ledger, or voucher writes, so retrying
                // cannot duplicate partially synchronized accounting data.
                while (!syncResult.IsSuccess &&
                       AutomationSyncRetryPolicy.ShouldRetry(syncResult, syncAttempt))
                {
                    ct.ThrowIfCancellationRequested();
                    var delay = AutomationSyncRetryPolicy.GetDelayBeforeRetry(syncAttempt);
                    var nextAttempt = syncAttempt + 1;

                    _logger.LogWarning(
                        "TallyPrime connection failed before data writes. Retrying automated sync (attempt {Attempt}/{MaximumAttempts}) in {DelaySeconds} seconds: {Reason}",
                        nextAttempt,
                        AutomationSyncRetryPolicy.MaximumAttempts,
                        delay.TotalSeconds,
                        syncResult.ErrorMessage);

                    SetProgress(
                        AuditAutomationStage.Synchronizing,
                        "RETRYING CONNECTION",
                        $"TallyPrime connection is temporarily unavailable. Retrying synchronization (attempt {nextAttempt} of {AutomationSyncRetryPolicy.MaximumAttempts}) in {delay.TotalSeconds:0} seconds. No accounting records have been written by this attempt.",
                        25);

                    await Task.Delay(delay, ct);
                    syncAttempt = nextAttempt;
                    syncResult = await _syncManager.StartSyncAsync(
                        company.TallyCompanyName,
                        SyncMode.Incremental,
                        ct);
                }

                ct.ThrowIfCancellationRequested();
                if (!syncResult.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"Synchronization failed before audit execution: {syncResult.ErrorMessage ?? "Unknown synchronization error"}");
                }

                SetProgress(AuditAutomationStage.Synchronizing, "SYNCHRONIZING",
                    $"Synchronization completed: {syncResult.TotalProcessed:N0} records processed.", 40);
            }

            ct.ThrowIfCancellationRequested();

            var period = await _companyContext.GetActivePeriodAsync(ct);
            var fromDate = period?.StartDate ?? company.BooksFromDate;
            var toDate = period?.EndDate ?? company.BooksFromDate.AddYears(1).AddDays(-1);
            var findings = 0;

            if (runFullAudit)
            {
                SetProgress(AuditAutomationStage.RunningAudit, "RUNNING AUDIT",
                    "Running all enabled audit rules and cross-dataset reconciliations...", 50);

                var results = await _auditEngine.ExecuteAuditAsync(
                    new AuditExecutionContext(company.Id, fromDate, toDate),
                    ct);

                findings = results.Count;

                var failedRules = _auditEngine.LastExecutionFailures ?? Array.Empty<AuditRuleFailure>();
                if (failedRules.Count > 0)
                {
                    var failedRuleDetails = string.Join("; ", failedRules.Select(f =>
                        string.IsNullOrWhiteSpace(f.ErrorMessage)
                            ? $"{f.RuleId} ({f.RuleName})"
                            : $"{f.RuleId} ({f.RuleName}): {f.ErrorMessage}"));
                    var incompleteMessage =
                        $"AUDIT INCOMPLETE: {failedRules.Count} rule(s) failed: {failedRuleDetails}. " +
                        $"{findings:N0} finding(s) were retained, but audit coverage is incomplete. Review the failures and rerun before relying on the results.";

                    _logger.LogError(
                        "Automated audit incomplete for company {Company}: {FailedRuleCount} rule(s) failed: {FailedRules}",
                        company.TallyCompanyName,
                        failedRules.Count,
                        string.Join(", ", failedRules.Select(f => f.RuleId)));

                    await RecordAsync("Automated audit workflow incomplete", incompleteMessage);
                    SetProgress(AuditAutomationStage.Incomplete, "AUDIT INCOMPLETE", incompleteMessage, 100, findings);
                    stopwatch.Stop();

                    return new AuditAutomationResult(
                        false,
                        company.TallyCompanyName,
                        _syncManager.CurrentMetrics.RecordsProcessed,
                        findings,
                        stopwatch.Elapsed,
                        incompleteMessage,
                        IsIncomplete: true);
                }

                SetProgress(AuditAutomationStage.PreparingResults, "PREPARING RESULTS",
                    $"Audit analysis completed. {findings:N0} finding(s) generated.", 90, findings);
            }

            await RecordAsync(
                "Automated audit workflow completed",
                $"Automation completed for '{company.TallyCompanyName}'. " +
                $"Synchronized latest data and generated {findings:N0} finding(s).");

            SetProgress(AuditAutomationStage.Completed, "COMPLETED",
                "Automation completed successfully. Review the findings dashboard.", 100, findings);

            return new AuditAutomationResult(
                true,
                company.TallyCompanyName,
                _syncManager.CurrentMetrics.RecordsProcessed,
                findings,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            SetProgress(AuditAutomationStage.Cancelled, "CANCELLED",
                "Automation was cancelled. TallyPrime data was not modified.", 100);
            return new AuditAutomationResult(
                false,
                _companyContext.ActiveCompanyName ?? string.Empty,
                _syncManager.CurrentMetrics.RecordsProcessed,
                0,
                stopwatch.Elapsed,
                "Automation cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automated audit workflow failed.");
            SetProgress(AuditAutomationStage.Failed, "AUTOMATION FAILED", ex.Message, 100);
            return new AuditAutomationResult(
                false,
                _companyContext.ActiveCompanyName ?? string.Empty,
                _syncManager.CurrentMetrics.RecordsProcessed,
                0,
                stopwatch.Elapsed,
                ex.Message);
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
            _runGate.Release();
        }
    }

    public async Task CancelAsync()
    {
        _runCts?.Cancel();

        try
        {
            if (_syncManager.CurrentStatus == SyncStatus.Running)
            {
                await _syncManager.CancelAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ignoring synchronization cancellation error.");
        }
    }

    private void SetProgress(
        AuditAutomationStage stage,
        string title,
        string message,
        double percentage,
        int findings = 0)
    {
        CurrentStage = stage;
        ProgressChanged?.Invoke(
            this,
            new AuditAutomationProgress(stage, title, message, percentage, findings));
    }

    private Task RecordAsync(string action, string description)
    {
        if (_auditTrailService == null)
        {
            return Task.CompletedTask;
        }

        return _auditTrailService.RecordActivityAsync(
            actionType: action,
            module: "AUTOMATION",
            description: description,
            companyName: _companyContext.ActiveCompanyName ?? string.Empty,
            ct: CancellationToken.None);
    }
}
