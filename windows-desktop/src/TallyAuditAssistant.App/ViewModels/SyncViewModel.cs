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

public partial class SyncViewModel : ObservableObject
{
    private readonly ISyncManager _syncManager;
    private readonly ITallyCompanyService _companyService;
    private readonly ISettingsService _settingsService;
    private readonly IActiveCompanyContext _companyContext;

    [ObservableProperty]
    private string _companyName = string.Empty;

    [ObservableProperty]
    private bool _isSyncing = false;

    [ObservableProperty]
    private bool _isPaused = false;

    [ObservableProperty]
    private string _currentStageText = "Idle";

    [ObservableProperty]
    private string _currentTaskDescription = "Ready to start synchronization.";

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

    private void OnActiveCompanyChanged(object? sender, Company? comp)
    {
        if (comp != null && !string.IsNullOrEmpty(comp.TallyCompanyName))
        {
            CompanyName = comp.TallyCompanyName;
            _ = LoadHistoryAsync();
        }
    }

    private async Task LoadInitialDataAsync()
    {
        try
        {
            var comp = await _companyContext.GetActiveCompanyAsync()
                       ?? await _companyContext.EnsureAndInitializeActiveCompanyAsync();

            if (comp != null && !string.IsNullOrEmpty(comp.TallyCompanyName))
            {
                CompanyName = comp.TallyCompanyName;
            }
            else
            {
                var active = await _settingsService.GetSettingAsync("ActiveCompany", string.Empty);
                if (string.IsNullOrEmpty(active))
                {
                    active = await _companyService.GetActiveCompanyAsync();
                }

                if (!string.IsNullOrEmpty(active))
                {
                    CompanyName = active;
                    await _settingsService.SetSettingAsync("ActiveCompany", active);
                }
                else
                {
                    CompanyName = string.Empty;
                }
            }

            await LoadHistoryAsync();
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
        if (string.IsNullOrEmpty(CompanyName))
        {
            var comp = await _companyContext.EnsureAndInitializeActiveCompanyAsync();
            CompanyName = comp?.TallyCompanyName ?? await _companyService.GetActiveCompanyAsync() ?? string.Empty;
        }

        if (string.IsNullOrEmpty(CompanyName)) return;

        IsSyncing = true;
        IsPaused = false;
        LiveLogs.Clear();
        await _syncManager.StartSyncAsync(CompanyName, SyncMode.Full);
        IsSyncing = false;
        await LoadInitialDataAsync();
    }

    [RelayCommand]
    private async Task StartIncrementalSyncAsync()
    {
        if (string.IsNullOrEmpty(CompanyName))
        {
            var comp = await _companyContext.EnsureAndInitializeActiveCompanyAsync();
            CompanyName = comp?.TallyCompanyName ?? await _companyService.GetActiveCompanyAsync() ?? string.Empty;
        }

        if (string.IsNullOrEmpty(CompanyName)) return;

        IsSyncing = true;
        IsPaused = false;
        LiveLogs.Clear();
        await _syncManager.StartSyncAsync(CompanyName, SyncMode.Incremental);
        IsSyncing = false;
        await LoadInitialDataAsync();
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
        await _syncManager.CancelSyncAsync();
        IsSyncing = false;
        IsPaused = false;
    }

    private void OnProgressChanged(object? sender, SyncMetrics metrics)
    {
        App.Current?.Dispatcher.Invoke(() =>
        {
            CurrentStageText = metrics.Stage.ToString();
            CurrentTaskDescription = metrics.StageDescription;
            RecordsDiscovered = metrics.TotalDiscovered;
            RecordsProcessed = metrics.TotalProcessed;
            RecordsInserted = metrics.Inserted;
            RecordsUpdated = metrics.Updated;
            RecordsSkipped = metrics.Skipped;
            Errors = metrics.Errors;
            ElapsedTimeText = $"{metrics.Elapsed.Minutes:D2}:{metrics.Elapsed.Seconds:D2}";
            ItemsPerSecond = Math.Round(metrics.ItemsPerSecond, 1);
            ProgressPercentage = metrics.TotalDiscovered > 0 ? (double)metrics.TotalProcessed / metrics.TotalDiscovered * 100 : 0;
        });
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
