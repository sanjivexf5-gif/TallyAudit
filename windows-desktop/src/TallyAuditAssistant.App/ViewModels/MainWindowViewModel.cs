using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ITallyConnection _tallyConnection;
    private readonly IActiveCompanyContext _companyContext;
    private readonly ITallyCompanyService _companyService;
    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private string _title = "Tally Audit Assistant — Auditor Edition";

    [ObservableProperty]
    private ObservableObject _currentViewModel;

    [ObservableProperty]
    private string _activeCompany = "No Company Selected";

    [ObservableProperty]
    private string _connectionStatusText = "Disconnected";

    [ObservableProperty]
    private string _connectionBadgeColor = "#EF4444"; // Red

    [ObservableProperty]
    private string _currentSection = "Dashboard";

    public DashboardViewModel DashboardVM { get; }
    public TallyConnectionViewModel ConnectionVM { get; }
    public SyncViewModel SyncVM { get; }
    public SettingsViewModel SettingsVM { get; }
    public CompaniesViewModel CompaniesVM { get; }
    public GstAuditViewModel GstVM { get; }
    public TdsAuditViewModel TdsVM { get; }
    public VouchersViewModel VouchersVM { get; }
    public LedgersViewModel LedgersVM { get; }
    public BankAuditViewModel BankVM { get; }
    public ExceptionsViewModel ExceptionsVM { get; }
    public ReportsViewModel ReportsVM { get; }
    public InvestigationViewModel InvestigationVM { get; }

    public MainWindowViewModel(
        ITallyConnection tallyConnection,
        IActiveCompanyContext companyContext,
        ITallyCompanyService companyService,
        ISettingsService settingsService,
        INavigationService navigationService,
        DashboardViewModel dashboardVM,
        TallyConnectionViewModel connectionVM,
        SyncViewModel syncVM,
        SettingsViewModel settingsVM,
        CompaniesViewModel companiesVM,
        GstAuditViewModel gstVM,
        TdsAuditViewModel tdsVM,
        VouchersViewModel vouchersVM,
        LedgersViewModel ledgersVM,
        BankAuditViewModel bankVM,
        ExceptionsViewModel exceptionsVM,
        ReportsViewModel reportsVM,
        InvestigationViewModel investigationVM)
    {
        _tallyConnection = tallyConnection;
        _companyContext = companyContext;
        _companyService = companyService;
        _settingsService = settingsService;
        _navigationService = navigationService;

        DashboardVM = dashboardVM;
        ConnectionVM = connectionVM;
        SyncVM = syncVM;
        SettingsVM = settingsVM;
        CompaniesVM = companiesVM;
        GstVM = gstVM;
        TdsVM = tdsVM;
        VouchersVM = vouchersVM;
        LedgersVM = ledgersVM;
        BankVM = bankVM;
        ExceptionsVM = exceptionsVM;
        ReportsVM = reportsVM;
        InvestigationVM = investigationVM;

        _currentViewModel = dashboardVM;

        _navigationService.Navigated += (s, sec) =>
        {
            if (CurrentSection != sec)
            {
                Navigate(sec);
            }
        };

        _companyContext.ActiveCompanyChanged += (s, comp) =>
        {
            void Update()
            {
                var compName = comp?.TallyCompanyName ?? _companyContext.TallyCompanyName ?? _companyContext.ActiveCompanyName;
                if (!string.IsNullOrEmpty(compName))
                {
                    ActiveCompany = compName;
                }
                else
                {
                    ActiveCompany = "No Company Selected";
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
        };

        _tallyConnection.StatusChanged += OnTallyStatusChanged;
        UpdateStatusDisplay(_tallyConnection.CurrentStatus);

        _ = InitializeActiveCompanyAsync();
    }

    private async Task InitializeActiveCompanyAsync()
    {
        var comp = await _companyContext.GetActiveCompanyAsync();
        var compName = comp?.TallyCompanyName ?? _companyContext.TallyCompanyName ?? _companyContext.ActiveCompanyName;
        if (!string.IsNullOrEmpty(compName))
        {
            ActiveCompany = compName;
        }
        else
        {
            ActiveCompany = "No Company Selected";
        }
    }

    [RelayCommand]
    public void Navigate(string section)
    {
        var activeName = _companyContext.TallyCompanyName ?? _companyContext.ActiveCompanyName;
        if (!string.IsNullOrEmpty(activeName) && ActiveCompany != activeName)
        {
            ActiveCompany = activeName;
        }

        ObservableObject? nextVM = section switch
        {
            "Dashboard" => DashboardVM,
            "TallyConnection" => ConnectionVM,
            "Companies" => CompaniesVM,
            "Sync" => SyncVM,
            "GST" => GstVM,
            "TDS" => TdsVM,
            "Vouchers" => VouchersVM,
            "Ledgers" => LedgersVM,
            "Bank" => BankVM,
            "Exceptions" => ExceptionsVM,
            "Investigation" => InvestigationVM,
            "Reports" => ReportsVM,
            "Settings" => SettingsVM,
            _ => null
        };

        if (nextVM != null)
        {
            CurrentSection = section;
            CurrentViewModel = nextVM;
            if (_navigationService.CurrentSection != section)
            {
                _navigationService.Navigate(section);
            }

            if (nextVM is INavigationAware navAware)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await navAware.OnNavigatedToAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error on navigating to {section}: {ex.Message}");
                    }
                });
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"Navigation error: Unknown section '{section}' requested.");
        }
    }

    private void OnTallyStatusChanged(object? sender, ConnectionStatus status)
    {
        App.Current.Dispatcher.Invoke(() => UpdateStatusDisplay(status));
    }

    private void UpdateStatusDisplay(ConnectionStatus status)
    {
        switch (status)
        {
            case ConnectionStatus.Connected:
                ConnectionStatusText = $"Connected (Port {_tallyConnection.ActiveEndpoint?.Port ?? 9000})";
                ConnectionBadgeColor = "#10B981"; // Green
                
                var currentActive = _companyContext.TallyCompanyName ?? _companyContext.ActiveCompanyName;
                if (!string.IsNullOrEmpty(currentActive))
                {
                    ActiveCompany = currentActive;
                }
                else
                {
                    ActiveCompany = "No Company Selected";
                }
                break;
            case ConnectionStatus.Scanning:
                ConnectionStatusText = "Scanning Ports (9000-9005)...";
                ConnectionBadgeColor = "#F59E0B"; // Amber
                break;
            case ConnectionStatus.ProcessRunningPortClosed:
                ConnectionStatusText = "Tally Open (HTTP Disabled)";
                ConnectionBadgeColor = "#F97316"; // Orange
                break;
            default:
                ConnectionStatusText = "Tally Disconnected";
                ConnectionBadgeColor = "#EF4444"; // Red
                if (string.IsNullOrEmpty(_companyContext.ActiveCompanyName))
                {
                    ActiveCompany = "No Company Selected";
                }
                break;
        }
    }
}
