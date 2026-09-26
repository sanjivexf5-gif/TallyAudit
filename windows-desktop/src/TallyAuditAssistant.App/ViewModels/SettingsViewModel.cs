using System.Threading;
using System.Threading.Tasks;
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
    private decimal _largeTransactionThreshold = 1000000;

    [ObservableProperty]
    private decimal _tdsSinglePaymentLimit = 30000;

    [ObservableProperty]
    private int _roundNumberMultiple = 10000;

    [ObservableProperty]
    private decimal _roundNumberMinAmount = 50000;

    [ObservableProperty]
    private int _periodEndReviewDays = 5;

    [ObservableProperty]
    private string _duplicateSensitivity = "High";

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

        var largeStr = await _settingsService.GetSettingAsync("LargeTransactionThreshold", "1000000");
        if (decimal.TryParse(largeStr, out var lt)) LargeTransactionThreshold = lt;

        var tdsStr = await _settingsService.GetSettingAsync("TdsSinglePaymentLimit", "30000");
        if (decimal.TryParse(tdsStr, out var ts)) TdsSinglePaymentLimit = ts;

        var multipleStr = await _settingsService.GetSettingAsync("RoundNumberMultiple", "10000");
        if (int.TryParse(multipleStr, out var rm)) RoundNumberMultiple = rm;

        var minStr = await _settingsService.GetSettingAsync("RoundNumberMinAmount", "50000");
        if (decimal.TryParse(minStr, out var rmin)) RoundNumberMinAmount = rmin;

        var daysStr = await _settingsService.GetSettingAsync("PeriodEndReviewDays", "5");
        if (int.TryParse(daysStr, out var pd)) PeriodEndReviewDays = pd;

        DuplicateSensitivity = await _settingsService.GetSettingAsync("DuplicateSensitivity", "High");
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        await _settingsService.SetTallyHostAsync(TallyHost);
        await _settingsService.SetTallyPortAsync(TallyPort);
        await _settingsService.SetMockModeEnabledAsync(IsMockMode);

        await _settingsService.SetSettingAsync("LargeTransactionThreshold", LargeTransactionThreshold.ToString());
        await _settingsService.SetSettingAsync("TdsSinglePaymentLimit", TdsSinglePaymentLimit.ToString());
        await _settingsService.SetSettingAsync("RoundNumberMultiple", RoundNumberMultiple.ToString());
        await _settingsService.SetSettingAsync("RoundNumberMinAmount", RoundNumberMinAmount.ToString());
        await _settingsService.SetSettingAsync("PeriodEndReviewDays", PeriodEndReviewDays.ToString());
        await _settingsService.SetSettingAsync("DuplicateSensitivity", DuplicateSensitivity);

        StatusMessage = "Settings saved successfully to local SQLite storage.";
    }
}
