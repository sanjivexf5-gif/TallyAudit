using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class AuditFinalizationViewModel : ObservableObject, INavigationAware
{
    private readonly IAuditFinalizationRepository _finalizationRepository;
    private readonly IAuditRepository _auditRepository;
    private readonly IActiveCompanyContext _companyContext;
    private readonly IAuditTrailService? _auditTrailService;
    private string _companyId = string.Empty;
    private string _financialPeriodId = string.Empty;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _activeCompanyName = "No Company Selected";
    [ObservableProperty] private string _financialYear = string.Empty;
    [ObservableProperty] private string _lifecycleStatus = "Draft";
    [ObservableProperty] private int _checklistCompletion;
    [ObservableProperty] private int _totalChecklist;
    [ObservableProperty] private int _completedChecklist;
    [ObservableProperty] private int _pendingHighRisk;
    [ObservableProperty] private int _openItems;
    [ObservableProperty] private int _totalEvidence;
    [ObservableProperty] private int _verifiedEvidence;
    [ObservableProperty] private int _rejectedEvidence;
    [ObservableProperty] private string _conclusionStatus = "No Exceptions Noted";
    [ObservableProperty] private string _conclusionText = string.Empty;
    [ObservableProperty] private string _reviewerName = string.Empty;
    [ObservableProperty] private string _reviewerComments = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isSuccess;

    public ObservableCollection<string> ReadinessIssues { get; } = new();

    public bool CanMarkReady =>
        ChecklistCompletion >= 100 &&
        PendingHighRisk == 0 &&
        OpenItems == 0 &&
        RejectedEvidence == 0;

    public bool CanFinalize =>
        LifecycleStatus == nameof(AuditLifecycleStatus.ReadyForFinalization) &&
        !string.IsNullOrWhiteSpace(ConclusionText);

    public IReadOnlyList<string> ConclusionStatuses { get; } = new[]
    {
        "No Exceptions Noted", "Exceptions Noted", "Further Review Required",
        "Unable to Complete", "Not Applicable"
    };

    public AuditFinalizationViewModel(
        IAuditFinalizationRepository finalizationRepository,
        IAuditRepository auditRepository,
        IActiveCompanyContext companyContext,
        IAuditTrailService? auditTrailService = null)
    {
        _finalizationRepository = finalizationRepository;
        _auditRepository = auditRepository;
        _companyContext = companyContext;
        _auditTrailService = auditTrailService;
        _companyContext.ActiveCompanyChanged += (_, _) => _ = LoadAsync();
        _ = LoadAsync();
    }

    public async Task OnNavigatedToAsync() => await LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        IsError = false;
        try
        {
            var company = await _companyContext.GetActiveCompanyAsync();
            if (company == null)
            {
                ActiveCompanyName = "No Company Selected";
                ResetMetrics();
                return;
            }

            _companyId = company.Id;
            ActiveCompanyName = company.TallyCompanyName;
            FinancialYear = ResolveFinancialYear(DateTime.Today);
            _financialPeriodId = $"FY-{FinancialYear}";

            var state = await _finalizationRepository.GetStateAsync(_companyId, _financialPeriodId);
            if (state == null)
            {
                state = new AuditFinalizationState
                {
                    CompanyId = _companyId,
                    FinancialPeriodId = _financialPeriodId,
                    Status = AuditLifecycleStatus.InProgress,
                    AuditorConclusionPreparedBy = Environment.UserName
                };
                await _finalizationRepository.SaveStateAsync(state);
            }

            LifecycleStatus = state.Status.ToString();
            ConclusionStatus = state.AuditorConclusionStatus;
            ConclusionText = state.AuditorConclusionText;
            ReviewerName = state.ReviewerName ?? string.Empty;
            ReviewerComments = state.ReviewerComments ?? string.Empty;

            var checklist = (await _finalizationRepository.GetChecklistAsync($"CHECKLIST-{company.Id}")).ToList();
            TotalChecklist = checklist.Count;
            CompletedChecklist = checklist.Count(x => x.Status == "Completed");
            var applicable = checklist.Count(x => x.Status != "Not Applicable");
            ChecklistCompletion = applicable == 0 ? 100 : (int)Math.Round(CompletedChecklist * 100.0 / applicable);

            var exceptions = await _auditRepository.GetExceptionsAsync(company.Id, take: 5000);
            PendingHighRisk = exceptions.Count(x =>
                (x.Severity == SeverityLevel.Critical || x.Severity == SeverityLevel.High) &&
                x.Status == ReviewStatus.Pending);

            var openItems = await _finalizationRepository.GetOpenItemsAsync($"CHECKLIST-{company.Id}");
            OpenItems = openItems.Count(x => !string.Equals(x.Status, "Resolved", StringComparison.OrdinalIgnoreCase));

            var evidence = (await _finalizationRepository.GetAuditEvidenceAsync(
                await _finalizationRepository.GetOrCreateWorkingPaperPlanIdAsync(company.Id, company.TallyCompanyName))).ToList();
            TotalEvidence = evidence.Count;
            VerifiedEvidence = evidence.Count(x => string.Equals(x.Status, "Verified", StringComparison.OrdinalIgnoreCase));
            RejectedEvidence = evidence.Count(x => string.Equals(x.Status, "Rejected", StringComparison.OrdinalIgnoreCase));

            RebuildReadinessIssues();
            NotifyReadiness();
            StatusMessage = $"Audit finalization status: {LifecycleStatus}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to load finalization workspace: {ex.Message}";
            IsError = true;
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task SaveConclusionAsync()
    {
        await SaveStateAsync(false);
    }

    [RelayCommand]
    public async Task MarkReadyForFinalizationAsync()
    {
        if (!CanMarkReady)
        {
            RebuildReadinessIssues();
            StatusMessage = "Audit is not ready for finalization. Resolve the readiness items first.";
            IsError = true;
            return;
        }

        LifecycleStatus = nameof(AuditLifecycleStatus.ReadyForFinalization);
        await SaveStateAsync(true);
    }

    [RelayCommand]
    public async Task FinalizeAuditAsync()
    {
        if (!CanFinalize)
        {
            StatusMessage = "Enter an auditor conclusion and move the audit to Ready for Finalization first.";
            IsError = true;
            return;
        }

        LifecycleStatus = nameof(AuditLifecycleStatus.Finalized);
        await SaveStateAsync(true, true);
    }

    [RelayCommand]
    public async Task GenerateFinalizationSummaryAsync()
    {
        IsLoading = true;
        IsSuccess = IsError = false;
        try
        {
            var company = await _companyContext.GetActiveCompanyAsync();
            if (company == null) throw new InvalidOperationException("Select a company first.");

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TallyAuditFinalization");
            Directory.CreateDirectory(dir);
            var safe = string.Join("_", company.TallyCompanyName.Split(Path.GetInvalidFileNameChars()));
            var file = Path.Combine(dir, $"Audit_Finalization_Summary_{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            var html = new StringBuilder();
            html.AppendLine("<!doctype html><html><head><meta charset='utf-8'><title>Audit Finalization Summary</title>");
            html.AppendLine("<style>body{font-family:Segoe UI,Arial;margin:42px;color:#172033}h1{text-align:center}table{border-collapse:collapse;width:100%}td,th{border:1px solid #ccd3df;padding:9px;text-align:left}.ok{font-weight:700}</style></head><body>");
            html.AppendLine($"<h1>Audit Finalization Summary</h1><p><b>Company:</b> {System.Net.WebUtility.HtmlEncode(company.TallyCompanyName)}<br><b>Financial Year:</b> {FinancialYear}<br><b>Status:</b> {LifecycleStatus}</p>");
            html.AppendLine("<table><tr><th>Readiness Area</th><th>Result</th></tr>");
            html.AppendLine($"<tr><td>Checklist completion</td><td>{ChecklistCompletion}% ({CompletedChecklist}/{TotalChecklist})</td></tr>");
            html.AppendLine($"<tr><td>Critical / High pending findings</td><td>{PendingHighRisk}</td></tr>");
            html.AppendLine($"<tr><td>Open finalization items</td><td>{OpenItems}</td></tr>");
            html.AppendLine($"<tr><td>Evidence</td><td>{VerifiedEvidence} verified / {TotalEvidence} total / {RejectedEvidence} rejected</td></tr>");
            html.AppendLine("</table>");
            html.AppendLine($"<h2>Auditor Conclusion</h2><p><b>{System.Net.WebUtility.HtmlEncode(ConclusionStatus)}</b></p><p>{System.Net.WebUtility.HtmlEncode(ConclusionText).Replace("\n","<br>")}</p>");
            html.AppendLine("<p><i>This document is a working-paper summary generated from Tally Audit Assistant. It does not modify TallyPrime data and does not replace professional audit judgement.</i></p></body></html>");
            await File.WriteAllTextAsync(file, html.ToString(), Encoding.UTF8);
            StatusMessage = $"Finalization summary generated: {file}";
            IsSuccess = true;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = file, UseShellExecute = true }); } catch { }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Summary generation failed: {ex.Message}";
            IsError = true;
        }
        finally { IsLoading = false; }
    }

    private async Task SaveStateAsync(bool lifecycleChange, bool finalized = false)
    {
        IsLoading = true;
        IsError = IsSuccess = false;
        try
        {
            var state = await _finalizationRepository.GetStateAsync(_companyId, _financialPeriodId)
                        ?? new AuditFinalizationState { CompanyId = _companyId, FinancialPeriodId = _financialPeriodId };

            state.Status = Enum.TryParse<AuditLifecycleStatus>(LifecycleStatus, out var status) ? status : AuditLifecycleStatus.InProgress;
            state.CompletionPercentage = ChecklistCompletion;
            state.AuditorConclusionStatus = ConclusionStatus;
            state.AuditorConclusionText = ConclusionText;
            state.AuditorConclusionPreparedBy = string.IsNullOrWhiteSpace(state.AuditorConclusionPreparedBy) ? Environment.UserName : state.AuditorConclusionPreparedBy;
            state.AuditorConclusionDate = DateTime.UtcNow;
            state.ReviewerName = string.IsNullOrWhiteSpace(ReviewerName) ? null : ReviewerName;
            state.ReviewerComments = string.IsNullOrWhiteSpace(ReviewerComments) ? null : ReviewerComments;
            state.ReviewedAt = string.IsNullOrWhiteSpace(ReviewerName) ? null : DateTime.UtcNow;
            if (finalized)
            {
                state.FinalizedBy = Environment.UserName;
                state.FinalizedAt = DateTime.UtcNow;
            }

            await _finalizationRepository.SaveStateAsync(state);

            if (_auditTrailService != null)
                await _auditTrailService.RecordActivityAsync(
                    actionType: finalized ? "Audit finalized" : lifecycleChange ? "Audit moved to finalization" : "Audit conclusion saved",
                    module: "AUDIT FINALIZATION",
                    description: $"Audit finalization status: {state.Status}. Conclusion: {state.AuditorConclusionStatus}.",
                    companyName: ActiveCompanyName,
                    ct: CancellationToken.None);

            LifecycleStatus = state.Status.ToString();
            StatusMessage = finalized ? "Audit finalized successfully." : "Audit finalization details saved.";
            IsSuccess = true;
            NotifyReadiness();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
            IsError = true;
        }
        finally { IsLoading = false; }
    }

    private void RebuildReadinessIssues()
    {
        ReadinessIssues.Clear();
        if (ChecklistCompletion < 100) ReadinessIssues.Add($"Complete the audit checklist ({ChecklistCompletion}% complete).");
        if (PendingHighRisk > 0) ReadinessIssues.Add($"Review or resolve {PendingHighRisk} critical/high pending finding(s).");
        if (OpenItems > 0) ReadinessIssues.Add($"Close {OpenItems} open finalization item(s).");
        if (RejectedEvidence > 0) ReadinessIssues.Add($"Replace or resolve {RejectedEvidence} rejected evidence item(s).");
        if (TotalEvidence > 0 && VerifiedEvidence < TotalEvidence - RejectedEvidence) ReadinessIssues.Add($"Verify outstanding evidence ({VerifiedEvidence}/{TotalEvidence} verified).");
        if (ReadinessIssues.Count == 0) ReadinessIssues.Add("All configured finalization readiness checks passed.");
    }

    private void NotifyReadiness()
    {
        OnPropertyChanged(nameof(CanMarkReady));
        OnPropertyChanged(nameof(CanFinalize));
    }

    private void ResetMetrics()
    {
        _companyId = _financialPeriodId = string.Empty;
        LifecycleStatus = "Draft";
        ChecklistCompletion = TotalChecklist = CompletedChecklist = PendingHighRisk = OpenItems = TotalEvidence = VerifiedEvidence = RejectedEvidence = 0;
        RebuildReadinessIssues();
        NotifyReadiness();
    }

    private static string ResolveFinancialYear(DateTime date) =>
        date.Month >= 4 ? $"{date.Year}-{(date.Year + 1) % 100:D2}" : $"{date.Year - 1}-{date.Year % 100:D2}";
}
