using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IDatabaseInitializer _dbInitializer;

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _tallyHost = "localhost";

    [ObservableProperty]
    private int _tallyPort = 9000;

    [ObservableProperty]
    private bool _isMockMode = false;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public SettingsViewModel(ISettingsService settingsService, IDatabaseInitializer dbInitializer)
    {
        _settingsService = settingsService;
        _dbInitializer = dbInitializer;
        _databasePath = _dbInitializer.DatabasePath;

        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        TallyHost = await _settingsService.GetTallyHostAsync();
        TallyPort = await _settingsService.GetTallyPortAsync();
        IsMockMode = await _settingsService.IsMockModeEnabledAsync();
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        await _settingsService.SetTallyHostAsync(TallyHost);
        await _settingsService.SetTallyPortAsync(TallyPort);
        await _settingsService.SetMockModeEnabledAsync(IsMockMode);

        StatusMessage = "Settings saved successfully to local SQLite storage.";
    }
}
