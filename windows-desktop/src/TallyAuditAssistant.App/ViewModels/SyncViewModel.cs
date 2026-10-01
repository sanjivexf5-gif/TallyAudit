using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Sync;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class SyncViewModel : ObservableObject, INavigationAware
{
    private readonly ISyncManager _syncManager;
    private readonly ITallyCompanyService _companyService;
    private readonly ISettingsService _settingsService;
    private readonly IActiveCompanyContext _companyContext;

    [ObservableProperty]
    private string _companyName = string.Empty;

    [ObservableProperty]
    private string _contextStatusText = "Context: Verified";

    [ObservableProperty]
    private bool _isSyncing = false;

    [ObservableProperty]
    private bool _isPaused = false;

    [ObservableProperty]
    private string _currentStageText = "Idle";

    [ObservableProperty]
    private string _currentTaskDescription = "Please select and save a Tally company before synchronization.";

    [ObservableProperty]
    private int _recordsDiscovered = 0;

    [ObservableProperty]
    private int _recordsProcessed = 0;

    [ObservableProperty]
    private int _recordsInserted = 0;

    [ObservableProperty]
    private int _recordsUpdated = 0;

    [ObservableProperty]
    private int _recordsSkipped = 0;

    [ObservableProperty]
    private int _errors = 0;

    [ObservableProperty]
    private string _elapsedTimeText = "00:00";

    [ObservableProperty]
    private double _itemsPerSecond = 0.0;

    [ObservableProperty]
    private double _progressPercentage = 0.0;

    public ObservableCollection<string> LiveLogs { get; } = new();
    public ObservableCollection<SyncHistoryRecord> SyncHistory { get; } = new();

    public SyncViewModel(
        ISyncManager syncManager,
        ITallyCompanyService companyService,
        ISettingsService settingsService,
        IActiveCompanyContext companyContext)
    {
        _syncManager = syncManager;
        _companyService = companyService;
        _settingsService = settingsService;
        _companyContext = companyContext;

        _syncManager.ProgressChanged += OnProgressChanged;
        _syncManager.SyncLogEmitted += OnLogEmitted;
        _companyContext.ActiveCompanyChanged += OnActiveCompanyChanged;

        _ = LoadInitialDataAsync();
    }

    public async Task OnNavigatedToAsync()
    {
        await LoadInitialDataAsync();
    }

    private void OnActiveCompanyChanged(object? sender, Company? comp)
    {
        Serilog.Log.Information("[SyncViewModel] SYNC VM RECEIVED COMPANY CHANGE: {Company} on context #{HashCode}",
            comp?.TallyCompanyName, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_companyContext));

        void Update()
        {
            var compName = comp?.TallyCompanyName ?? _companyContext.TallyCompanyName ?? _companyContext.ActiveCompanyName;
            if (!string.IsNullOrEmpty(compName))
            {
                CompanyName = compName;
                ContextStatusText = "Context: Verified";
                if (!IsSyncing)
                {
                    CurrentTaskDescription = $"Ready to synchronize {compName}.";
                }
                _ = LoadHistoryAsync();
            }
            else
            {
                CompanyName = string.Empty;
                ContextStatusText = "Context: Not Set";
                CurrentTaskDescription = "Please select and save a Tally company before synchronization.";
                SyncHistory.Clear();
            }
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

    private async Task LoadInitialDataAsync()
    {
        try
        {
            var activeCompany = await _companyContext.GetActiveCompanyAsync();
            var compName = activeCompany?.TallyCompanyName ?? _companyContext.TallyCompanyName ?? _companyContext.ActiveCompanyName;

            if (!string.IsNullOrEmpty(compName))
            {
                CompanyName = compName;
                ContextStatusText = "Context: Verified";
                if (!IsSyncing)
                {
                    CurrentTaskDescription = $"Ready to synchronize {compName}.";
                }
                await LoadHistoryAsync();
            }
            else
            {
                CompanyName = string.Empty;
                ContextStatusText = "Context: Not Set";
                CurrentTaskDescription = "Please select and save a Tally company before synchronization.";
                SyncHistory.Clear();
            }
        }
        catch
        {
            // Non-critical startup load
        }
    }

    private async Task LoadHistoryAsync()
    {
        if (string.IsNullOrEmpty(CompanyName)) return;
        try
        {
            var history = await _syncManager.GetSyncHistoryAsync(CompanyName);
            SyncHistory.Clear();
            foreach (var h in history)
            {
                SyncHistory.Add(h);
            }
        }
        catch
        {
            // Ignore history load errors during rapid switching
        }
    }

    [RelayCommand]
    private async Task StartFullSyncAsync()
    {
        if (IsSyncing) return;

        var comp = await _companyContext.GetActiveCompanyAsync();
        if (comp == null || string.IsNullOrWhiteSpace(comp.TallyCompanyName))
        {
            CurrentTaskDescription = "Please select and save a Tally company before synchronization.";
            return;
        }

        var companyNameToSync = comp.TallyCompanyName;
        CompanyName = companyNameToSync;

        // Hard verification before sync: verify context consistency
        var propActive = _companyContext.ActiveCompanyName;
        var propTally = _companyContext.TallyCompanyName;
        if (!string.IsNullOrEmpty(propActive) && !string.Equals(propActive, companyNameToSync, StringComparison.OrdinalIgnoreCase))
        {
            CurrentTaskDescription = $"SYNC ABORTED. Context company mismatch: ActiveCompanyName ('{propActive}') != Target ('{companyNameToSync}').";
            return;
        }
        if (!string.IsNullOrEmpty(propTally) && !string.Equals(propTally, companyNameToSync, StringComparison.OrdinalIgnoreCase))
        {
            CurrentTaskDescription = $"SYNC ABORTED. Context company mismatch: TallyCompanyName ('{propTally}') != Target ('{companyNameToSync}').";
            return;
        }

        IsSyncing = true;
        IsPaused = false;
        CurrentStageText = "Connecting";
        CurrentTaskDescription = $"Starting full synchronization for '{CompanyName}'...";
        LiveLogs.Clear();
        try
        {
            var result = await _syncManager.StartSyncAsync(CompanyName, SyncMode.Full);
            if (result != null)
            {
                if (result.IsSuccess)
                {
                    if (result.TotalProcessed == 0)
                    {
                        CurrentTaskDescription = $"SYNC COMPLETED WITH ZERO RECORDS. Company: {CompanyName}. Verified successful connectivity, but no records were found.";
                    }
                    else if (result.Errors > 0)
                    {
                        CurrentTaskDescription = $"SYNC COMPLETED WITH WARNINGS. Company: {CompanyName}. Processed {result.TotalProcessed} records ({result.Inserted} inserted, {result.Updated} updated) with {result.Errors} warnings.";
                    }
                    else
                    {
                        CurrentTaskDescription = $"SYNC SUCCESS. Company: {CompanyName}. Processed {result.TotalProcessed} records ({result.Inserted} inserted, {result.Updated} updated).";
                    }
                }
                else
                {
                    CurrentTaskDescription = $"SYNC FAILED. Stage: {_syncManager.CurrentMetrics.CurrentStage}. Company: {CompanyName}. Reason: {result.ErrorMessage ?? "Unknown error occurred"}";
                }
            }
        }
        catch (Exception ex)
        {
            CurrentTaskDescription = $"SYNC FAILED. Company: {CompanyName}. Exception: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }

        await LoadHistoryAsync();
    }

    [RelayCommand]
    private async Task StartIncrementalSyncAsync()
    {
        if (IsSyncing) return;

        var comp = await _companyContext.GetActiveCompanyAsync();
        if (comp == null || string.IsNullOrWhiteSpace(comp.TallyCompanyName))
        {
            CurrentTaskDescription = "Please select and save a Tally company before synchronization.";
            return;
        }

        var companyNameToSync = comp.TallyCompanyName;
        CompanyName = companyNameToSync;

        // Hard verification before sync: verify context consistency
        var propActive = _companyContext.ActiveCompanyName;
        var propTally = _companyContext.TallyCompanyName;
        if (!string.IsNullOrEmpty(propActive) && !string.Equals(propActive, companyNameToSync, StringComparison.OrdinalIgnoreCase))
        {
            CurrentTaskDescription = $"SYNC ABORTED. Context company mismatch: ActiveCompanyName ('{propActive}') != Target ('{companyNameToSync}').";
            return;
        }
        if (!string.IsNullOrEmpty(propTally) && !string.Equals(propTally, companyNameToSync, StringComparison.OrdinalIgnoreCase))
        {
            CurrentTaskDescription = $"SYNC ABORTED. Context company mismatch: TallyCompanyName ('{propTally}') != Target ('{companyNameToSync}').";
            return;
        }

        IsSyncing = true;
        IsPaused = false;
        CurrentStageText = "Connecting";
        CurrentTaskDescription = $"Starting incremental synchronization for '{CompanyName}'...";
        LiveLogs.Clear();
        try
        {
            var result = await _syncManager.StartSyncAsync(CompanyName, SyncMode.Incremental);
            if (result != null)
            {
                if (result.IsSuccess)
                {
                    if (result.TotalProcessed == 0)
                    {
                        CurrentTaskDescription = $"SYNC COMPLETED WITH ZERO RECORDS. Company: {CompanyName}. Verified successful connectivity, but no new records were found.";
                    }
                    else if (result.Errors > 0)
                    {
                        CurrentTaskDescription = $"SYNC COMPLETED WITH WARNINGS. Company: {CompanyName}. Processed {result.TotalProcessed} records ({result.Inserted} inserted, {result.Updated} updated) with {result.Errors} warnings.";
                    }
                    else
                    {
                        CurrentTaskDescription = $"SYNC SUCCESS. Company: {CompanyName}. Processed {result.TotalProcessed} records ({result.Inserted} inserted, {result.Updated} updated).";
                    }
                }
                else
                {
                    CurrentTaskDescription = $"SYNC FAILED. Stage: {_syncManager.CurrentMetrics.CurrentStage}. Company: {CompanyName}. Reason: {result.ErrorMessage ?? "Unknown error occurred"}";
                }
            }
        }
        catch (Exception ex)
        {
            CurrentTaskDescription = $"SYNC FAILED. Company: {CompanyName}. Exception: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }

        await LoadHistoryAsync();
    }

    [RelayCommand]
    private async Task RetrySyncAsync()
    {
        if (IsSyncing) return;

        var comp = await _companyContext.GetActiveCompanyAsync();
        if (comp == null || string.IsNullOrWhiteSpace(comp.TallyCompanyName))
        {
            CurrentTaskDescription = "Please select a Tally company before synchronization.";
            return;
        }

        CompanyName = comp.TallyCompanyName;

        IsSyncing = true;
        IsPaused = false;
        LiveLogs.Clear();
        try
        {
            var result = await _syncManager.RetryAsync();
            if (result != null)
            {
                if (result.IsSuccess)
                {
                    if (result.TotalProcessed == 0)
                    {
                        CurrentTaskDescription = $"SYNC COMPLETED WITH ZERO RECORDS. Company: {CompanyName}. Verified successful connectivity, but no records were found.";
                    }
                    else if (result.Errors > 0)
                    {
                        CurrentTaskDescription = $"SYNC COMPLETED WITH WARNINGS. Company: {CompanyName}. Processed {result.TotalProcessed} records ({result.Inserted} inserted, {result.Updated} updated) with {result.Errors} warnings.";
                    }
                    else
                    {
                        CurrentTaskDescription = $"SYNC SUCCESS. Company: {CompanyName}. Processed {result.TotalProcessed} records ({result.Inserted} inserted, {result.Updated} updated).";
                    }
                }
                else
                {
                    CurrentTaskDescription = $"SYNC FAILED. Stage: {_syncManager.CurrentMetrics.CurrentStage}. Company: {CompanyName}. Reason: {result.ErrorMessage ?? "Unknown error occurred"}";
                }
            }
        }
        catch (Exception ex)
        {
            CurrentTaskDescription = $"SYNC FAILED. Company: {CompanyName}. Exception: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }

        await LoadHistoryAsync();
    }

    [RelayCommand]
    private async Task PauseSyncAsync()
    {
        await _syncManager.PauseAsync();
        IsPaused = true;
    }

    [RelayCommand]
    private async Task ResumeSyncAsync()
    {
        await _syncManager.ResumeAsync();
        IsPaused = false;
    }

    [RelayCommand]
    private async Task CancelSyncAsync()
    {
        await _syncManager.CancelAsync();
        IsSyncing = false;
        IsPaused = false;
    }

    private void OnProgressChanged(object? sender, SyncMetrics metrics)
    {
        void Update()
        {
            CurrentStageText = metrics.CurrentStage.ToString();
            CurrentTaskDescription = metrics.CurrentTaskDescription;
            RecordsDiscovered = metrics.RecordsDiscovered;
            RecordsProcessed = metrics.RecordsProcessed;
            RecordsInserted = metrics.RecordsInserted;
            RecordsUpdated = metrics.RecordsUpdated;
            RecordsSkipped = metrics.RecordsSkipped;
            Errors = metrics.Errors;
            ElapsedTimeText = $"{metrics.ElapsedTime.Minutes:D2}:{metrics.ElapsedTime.Seconds:D2}";
            ItemsPerSecond = Math.Round(metrics.ItemsPerSecond, 1);
            if (metrics.CurrentStage == SyncStage.Complete)
            {
                ProgressPercentage = 100.0;
            }
            else
            {
                ProgressPercentage = metrics.ProgressPercentage;
            }
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

    private void OnLogEmitted(object? sender, string log)
    {
        App.Current?.Dispatcher.Invoke(() =>
        {
            LiveLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {log}");
            if (LiveLogs.Count > 200)
            {
                LiveLogs.RemoveAt(LiveLogs.Count - 1);
            }
        });
    }
}
