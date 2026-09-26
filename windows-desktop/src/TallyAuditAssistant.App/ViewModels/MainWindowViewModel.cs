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

    public MainWindowViewModel(
        ITallyConnection tallyConnection,
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
        ReportsViewModel reportsVM)
    {
        _tallyConnection = tallyConnection;
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

        _currentViewModel = dashboardVM;

        _tallyConnection.StatusChanged += OnTallyStatusChanged;
        UpdateStatusDisplay(_tallyConnection.CurrentStatus);
    }

    [RelayCommand]
    private void Navigate(string section)
    {
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
            "Reports" => ReportsVM,
            "Settings" => SettingsVM,
            _ => null
        };

        if (nextVM != null)
        {
            CurrentSection = section;
            CurrentViewModel = nextVM;
        }
        else
        {
            // Log the navigation error and keep current valid section
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
                ActiveCompany = _tallyConnection.ActiveEndpoint?.ActiveCompany ?? "Active Tally Session";
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
                break;
        }
    }
}
