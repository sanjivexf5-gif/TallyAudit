using System;
using System.Threading;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.App.Services;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class AuditAutomationViewModel : ObservableObject, INavigationAware, IDisposable
{
    private readonly IAuditAutomationService _automationService;
    private readonly INavigationService _navigationService;
    private readonly IActiveCompanyContext _companyContext;
    private readonly AuditReportPackViewModel? _reportPackViewModel;
    private readonly ScheduledAuditTaskService _scheduledAuditTaskService;
    private readonly AutomationRunHistoryService _runHistoryService;
    private readonly AutomationReadinessService? _readinessService;

    public ObservableCollection<AutomationRunHistoryEntry> RecentRuns { get; } = new();

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _companyName = "No Company Selected";
    [ObservableProperty] private string _stageTitle = "READY";
    [ObservableProperty] private string _statusMessage = "Click Start Automated Audit to run the complete audit workflow.";
    [ObservableProperty] private double _progressPercentage;
    [ObservableProperty] private int _findings;
    [ObservableProperty] private int _recordsSynchronized;
    [ObservableProperty] private string _elapsedTime = "00:00";
    [ObservableProperty] private bool _generateReport = true;
    [ObservableProperty] private bool _goToFindingsWhenComplete = true;
    [ObservableProperty] private string _scheduleTime = "02:00";
    [ObservableProperty] private string _scheduleFrequency = "Daily";
    [ObservableProperty] private string _scheduleStatus = "No scheduled audit is configured.";
    [ObservableProperty] private bool _isScheduleBusy;
    [ObservableProperty] private bool _isCheckingReadiness;
    [ObservableProperty] private string _readinessMessage = "Check automation readiness before starting to verify TallyPrime, company, audit period, and local storage.";

    private DateTime _startedAt;

    public AuditAutomationViewModel(
        IAuditAutomationService automationService,
        INavigationService navigationService,
        IActiveCompanyContext companyContext,
        AuditReportPackViewModel? reportPackViewModel = null,
        ScheduledAuditTaskService? scheduledAuditTaskService = null,
        AutomationRunHistoryService? runHistoryService = null,
        AutomationReadinessService? readinessService = null)
    {
        _automationService = automationService;
        _navigationService = navigationService;
        _companyContext = companyContext;
        _reportPackViewModel = reportPackViewModel;
        _scheduledAuditTaskService = scheduledAuditTaskService ?? new ScheduledAuditTaskService();
        _runHistoryService = runHistoryService ?? new AutomationRunHistoryService();
        _readinessService = readinessService;
        RefreshRunHistory();

        _automationService.ProgressChanged += OnProgressChanged;
        _companyContext.ActiveCompanyChanged += OnCompanyChanged;

        RefreshCompany();
    }

    public async Task OnNavigatedToAsync()
    {
        RefreshCompany();
        await RefreshScheduleAsync();
    }

    [RelayCommand]
    private async Task CheckReadinessAsync()
    {
        if (IsCheckingReadiness || IsRunning)
        {
            return;
        }

        if (_readinessService == null)
        {
            ReadinessMessage = "Automation readiness checks are unavailable. Restart the application and try again.";
            return;
        }

        IsCheckingReadiness = true;
        ReadinessMessage = "Checking TallyPrime connection, active company, audit period, and local storage...";

        try
        {
            var result = await _readinessService.CheckAsync();
            ReadinessMessage = result.Report;
        }
        catch (Exception ex)
        {
            ReadinessMessage = $"Readiness check could not complete: {ex.Message}";
        }
        finally
        {
            IsCheckingReadiness = false;
        }
    }

    [RelayCommand]
    private async Task StartAutomatedAuditAsync()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        Findings = 0;
        RecordsSynchronized = 0;
        ProgressPercentage = 0;
        StageTitle = "STARTING";
        StatusMessage = "Preparing the automated audit workflow...";
        _startedAt = DateTime.Now;

        try
        {
            var result = await _automationService.RunAsync(
                runIncrementalSync: true,
                runFullAudit: true);

            CompanyName = string.IsNullOrWhiteSpace(result.CompanyName)
                ? CompanyName
                : result.CompanyName;

            RecordsSynchronized = result.RecordsSynchronized;
            Findings = result.FindingsGenerated;
            ElapsedTime = $"{(int)result.Duration.TotalMinutes:D2}:{result.Duration.Seconds:D2}";

            if (result.IsSuccess)
            {
                StatusMessage =
                    $"Completed automatically: {RecordsSynchronized:N0} records synchronized, " +
                    $"{Findings:N0} finding(s) generated.";

                // Generate the existing board-ready report pack automatically so
                // the auditor does not need a second workflow step.
                if (GenerateReport && _reportPackViewModel != null)
                {
                    StatusMessage += " Generating audit report pack...";
                    await _reportPackViewModel.GeneratePackAsync();
                    StatusMessage = $"Automation complete. {Findings:N0} finding(s) generated and report pack prepared.";
                }

                RecordRun("Success", null);

                if (GoToFindingsWhenComplete)
                {
                    _navigationService.Navigate("Exceptions");
                }
            }
            else
            {
                StatusMessage = result.ErrorMessage ?? "Automation did not complete.";
                RecordRun(result.IsIncomplete ? "Incomplete" : "Failed", StatusMessage);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Automation failed: {ex.Message}";
            RecordRun("Failed", ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task SaveScheduleAsync()
    {
        if (IsScheduleBusy)
        {
            return;
        }

        IsScheduleBusy = true;
        try
        {
            await _scheduledAuditTaskService.CreateOrUpdateAsync(ScheduleFrequency, ScheduleTime);
            ScheduleStatus = $"Scheduled audit saved: {ScheduleFrequency} at {ScheduleTime}. It can run while this app is closed, provided you are signed in to Windows and TallyPrime is available.";
        }
        catch (Exception ex)
        {
            ScheduleStatus = $"Could not save schedule: {ex.Message}";
        }
        finally
        {
            IsScheduleBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveScheduleAsync()
    {
        if (IsScheduleBusy)
        {
            return;
        }

        IsScheduleBusy = true;
        try
        {
            await _scheduledAuditTaskService.RemoveAsync();
            ScheduleStatus = "Scheduled audit task removed.";
        }
        catch (Exception ex)
        {
            ScheduleStatus = $"Could not remove schedule: {ex.Message}";
        }
        finally
        {
            IsScheduleBusy = false;
        }
    }

    private async Task RefreshScheduleAsync()
    {
        try
        {
            var schedule = await _scheduledAuditTaskService.GetScheduleAsync();
            if (schedule is null)
            {
                ScheduleStatus = "No scheduled audit is configured.";
                return;
            }

            ScheduleFrequency = schedule.Frequency;
            ScheduleTime = schedule.Time;
            ScheduleStatus = $"Existing schedule detected: {ScheduleFrequency} at {ScheduleTime}.";
        }
        catch (Exception ex)
        {
            ScheduleStatus = $"Could not read the saved schedule: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        StatusMessage = "Stopping automation safely...";
        await _automationService.CancelAsync();
    }

    private void RecordRun(string status, string? details)
    {
        try
        {
            _runHistoryService.RecordRun(new AutomationRunHistoryEntry(
                _startedAt == default ? DateTime.Now : _startedAt,
                CompanyName,
                "Manual",
                status,
                RecordsSynchronized,
                Findings,
                _startedAt == default ? 0 : (DateTime.Now - _startedAt).TotalSeconds,
                details));

            RefreshRunHistory();
        }
        catch (Exception ex)
        {
            // History must never interrupt or turn a completed audit into a failure.
            StatusMessage += $" Run history could not be saved: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ExportRunHistory()
    {
        var entries = _runHistoryService.GetRecentRuns(100);
        if (entries.Count == 0)
        {
            StatusMessage = "There are no automation runs to export yet.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export automation run history",
            FileName = $"TallyAuditAutomationHistory_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            OverwritePrompt = true,
            RestoreDirectory = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            AutomationRunHistoryCsvExporter.WriteToFile(dialog.FileName, entries);
            StatusMessage = $"Exported {entries.Count} automation run(s) to {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not export automation run history: {ex.Message}";
        }
    }

    private void RefreshRunHistory()
    {
        RecentRuns.Clear();
        foreach (var entry in _runHistoryService.GetRecentRuns(10))
        {
            RecentRuns.Add(entry);
        }
    }

    private void OnProgressChanged(object? sender, AuditAutomationProgress progress)
    {
        void Update()
        {
            StageTitle = progress.StageTitle;
            StatusMessage = progress.Message;
            ProgressPercentage = progress.Percentage;
            Findings = progress.Findings;
            ElapsedTime = _startedAt == default
                ? "00:00"
                : $"{(int)(DateTime.Now - _startedAt).TotalMinutes:D2}:{(DateTime.Now - _startedAt).Seconds:D2}";
        }

        if (App.Current?.Dispatcher != null && !App.Current.Dispatcher.CheckAccess())
        {
            App.Current.Dispatcher.Invoke(Update);
        }
        else
        {
            Update();
        }
    }

    private void OnCompanyChanged(object? sender, TallyAuditAssistant.Core.Domain.Companies.Company? company)
    {
        RefreshCompany();
    }

    private void RefreshCompany()
    {
        var name = _companyContext.TallyCompanyName ?? _companyContext.ActiveCompanyName;
        if (!string.IsNullOrWhiteSpace(name))
        {
            CompanyName = name;
        }
    }

    public void Dispose()
    {
        _automationService.ProgressChanged -= OnProgressChanged;
        _companyContext.ActiveCompanyChanged -= OnCompanyChanged;
    }
}
