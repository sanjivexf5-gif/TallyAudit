using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Common;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Core.Licensing;

namespace TallyAuditAssistant.App.ViewModels;

public partial class SettingsViewModel : ObservableObject, INavigationAware
{
    private readonly ISettingsService _settingsService;
    private readonly IDatabaseInitializer _dbInitializer;
    private readonly IActiveCompanyContext _companyContext;
    private readonly IUpdateService _updateService;
    private readonly IAuditRepository _repository;
    private readonly ITallyConnection _tallyConnection;

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

    // Update system properties
    [ObservableProperty]
    private string _appVersionDisplay = AppVersion.FullVersionString;

    [ObservableProperty]
    private string _currentVersionNumber = AppVersion.Version;

    [ObservableProperty]
    private string _updateStatusMessage = "Press 'Check for Updates' to query GitHub Releases.";

    [ObservableProperty]
    private string _lastUpdateCheckText = "Never";

    [ObservableProperty]
    private bool _isCheckingUpdate = false;

    [ObservableProperty]
    private bool _isUpdateAvailable = false;

    [ObservableProperty]
    private string _newVersionText = string.Empty;

    [ObservableProperty]
    private bool _isDownloadingUpdate = false;

    [ObservableProperty]
    private double _downloadProgress = 0;

    private UpdateInfo? _latestUpdateInfo;

    public SettingsViewModel(
        ISettingsService settingsService, 
        IDatabaseInitializer dbInitializer, 
        IActiveCompanyContext companyContext,
        IUpdateService updateService,
        IAuditRepository repository,
        ITallyConnection tallyConnection)
    {
        _settingsService = settingsService;
        _dbInitializer = dbInitializer;
        _companyContext = companyContext;
        _updateService = updateService;
        _repository = repository;
        _tallyConnection = tallyConnection;
        _databasePath = _dbInitializer.DatabasePath;

        _ = LoadSettingsAsync();
    }

    public async Task OnNavigatedToAsync()
    {
        await LoadSettingsAsync();
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

        var lastCheck = await _settingsService.GetSettingAsync("LastUpdateCheckTime", "");
        if (!string.IsNullOrEmpty(lastCheck))
        {
            LastUpdateCheckText = lastCheck;
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
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

            if (IsMockMode)
            {
                await _companyContext.EnsureAndInitializeActiveCompanyAsync();
                await _tallyConnection.ProbePortRangeAsync(TallyHost, TallyPort, TallyPort);
                StatusMessage = "Mock Tally Integration enabled. Demo dataset is available.";
            }
            else
            {
                await _repository.ClearMockDatasetAsync();
                await _tallyConnection.ProbePortRangeAsync(TallyHost, TallyPort, TallyPort);
                StatusMessage = "Mock Tally Integration disabled. Demo data cleared.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save settings: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdate || IsDownloadingUpdate) return;

        IsCheckingUpdate = true;
        UpdateStatusMessage = "Checking for updates from official GitHub release feed...";

        try
        {
            var updateInfo = await _updateService.CheckForUpdatesAsync();
            _latestUpdateInfo = updateInfo;

            var checkTimeStr = DateTime.Now.ToString("dd-MMM-yyyy HH:mm:ss");
            LastUpdateCheckText = checkTimeStr;
            await _settingsService.SetSettingAsync("LastUpdateCheckTime", checkTimeStr);

            if (updateInfo.IsUpdateAvailable)
            {
                IsUpdateAvailable = true;
                NewVersionText = $"v{updateInfo.LatestVersion}";
                UpdateStatusMessage = $"Version {updateInfo.LatestVersion} is available! (Current: {updateInfo.CurrentVersion})";
            }
            else
            {
                IsUpdateAvailable = false;
                NewVersionText = string.Empty;
                UpdateStatusMessage = "You are using the latest version of Tally Audit Assistant.";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"Unable to check for updates. You can continue using the current version. ({ex.Message})";
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task DownloadAndInstallUpdateAsync()
    {
        if (_latestUpdateInfo == null || IsDownloadingUpdate) return;

        IsDownloadingUpdate = true;
        DownloadProgress = 0;
        UpdateStatusMessage = "Downloading latest installer...";

        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                UpdateStatusMessage = $"Downloading update: {p:F0}%";
            });

            var installerPath = await _updateService.DownloadUpdateAsync(_latestUpdateInfo, progress);
            if (string.IsNullOrEmpty(installerPath) || !System.IO.File.Exists(installerPath))
            {
                UpdateStatusMessage = "Update failed: Downloaded file not found.";
                return;
            }

            UpdateStatusMessage = "Verifying installer package integrity...";
            var isValid = await _updateService.VerifyUpdatePackageAsync(installerPath, _latestUpdateInfo.Sha256Checksum);
            if (!isValid)
            {
                UpdateStatusMessage = "Update failed: Installer package verification failed.";
                return;
            }

            UpdateStatusMessage = "Launching installer and preparing upgrade...";
            await _updateService.LaunchInstallerAndExitAsync(installerPath);

            System.Windows.Application.Current?.Shutdown(0);
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }
}
