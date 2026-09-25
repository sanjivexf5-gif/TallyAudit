using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Sync;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class SyncViewModel : ObservableObject
{
    private readonly ISyncManager _syncManager;
    private readonly ITallyCompanyService _companyService;

    [ObservableProperty]
    private string _companyName = "Apex Industrial Solutions Pvt Ltd";

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

    public SyncViewModel(ISyncManager syncManager, ITallyCompanyService companyService)
    {
        _syncManager = syncManager;
        _companyService = companyService;

        _syncManager.ProgressChanged += OnProgressChanged;
        _syncManager.SyncLogEmitted += OnLogEmitted;

        _ = LoadInitialDataAsync();
    }

    private async Task LoadInitialDataAsync()
    {
        try
        {
            var active = await _companyService.GetActiveCompanyAsync();
            if (!string.IsNullOrEmpty(active))
            {
                CompanyName = active;
            }

            var history = await _syncManager.GetSyncHistoryAsync(CompanyName);
            SyncHistory.Clear();
            foreach (var h in history)
            {
                SyncHistory.Add(h);
            }
        }
        catch
        {
            // Non-critical startup load
        }
    }

    [RelayCommand]
    public async Task StartFullSyncAsync()
    {
        IsSyncing = true;
        IsPaused = false;
        LiveLogs.Clear();
        await _syncManager.StartSyncAsync(CompanyName, SyncMode.Full);
        IsSyncing = false;
        await LoadInitialDataAsync();
    }

    [RelayCommand]
    public async Task StartIncrementalSyncAsync()
    {
        IsSyncing = true;
        IsPaused = false;
        LiveLogs.Clear();
        await _syncManager.StartSyncAsync(CompanyName, SyncMode.Incremental);
        IsSyncing = false;
        await LoadInitialDataAsync();
    }

    [RelayCommand]
    public async Task PauseSyncAsync()
    {
        await _syncManager.PauseAsync();
        IsPaused = true;
    }

    [RelayCommand]
    public async Task ResumeSyncAsync()
    {
        await _syncManager.ResumeAsync();
        IsPaused = false;
    }

    [RelayCommand]
    public async Task CancelSyncAsync()
    {
        await _syncManager.CancelAsync();
        IsSyncing = false;
        IsPaused = false;
    }

    [RelayCommand]
    public async Task RetrySyncAsync()
    {
        IsSyncing = true;
        IsPaused = false;
        await _syncManager.RetryAsync();
        IsSyncing = false;
        await LoadInitialDataAsync();
    }

    private void OnProgressChanged(object? sender, SyncMetrics metrics)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            CurrentStageText = metrics.CurrentStage.ToString();
            CurrentTaskDescription = metrics.CurrentTaskDescription;
            RecordsDiscovered = metrics.RecordsDiscovered;
            RecordsProcessed = metrics.RecordsProcessed;
            RecordsInserted = metrics.RecordsInserted;
            RecordsUpdated = metrics.RecordsUpdated;
            RecordsSkipped = metrics.RecordsSkipped;
            Errors = metrics.Errors;
            ElapsedTimeText = metrics.ElapsedTime.ToString(@"mm\:ss");
            ItemsPerSecond = metrics.ItemsPerSecond;
            ProgressPercentage = metrics.ProgressPercentage;
        });
    }

    private void OnLogEmitted(object? sender, string log)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            LiveLogs.Insert(0, log);
            if (LiveLogs.Count > 200) LiveLogs.RemoveAt(LiveLogs.Count - 1);
        });
    }
}
