using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ITallyConnection _tallyConnection;
    private readonly IActiveCompanyContext _companyContext;
    private readonly ITallyCompanyService _companyService;
    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigationService;
    private long _companyRefreshGeneration;

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
    public WorkingPapersViewModel WorkingPapersVM { get; }
    public AuditChecklistViewModel AuditChecklistVM { get; }
    public InvestigationViewModel InvestigationVM { get; }
    public AuditTrailViewModel AuditTrailVM { get; }
    public ManagementRepresentationLetterViewModel? ManagementRepresentationLetterVM { get; }
    public AuditEvidenceViewModel? AuditEvidenceVM { get; }
    public AuditFinalizationViewModel? AuditFinalizationVM { get; }

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
        WorkingPapersViewModel workingPapersVM,
        AuditChecklistViewModel auditChecklistVM,
        InvestigationViewModel investigationVM,
        AuditTrailViewModel auditTrailVM,
        ManagementRepresentationLetterViewModel? managementRepresentationLetterVM = null,
        AuditEvidenceViewModel? auditEvidenceVM = null,
        AuditFinalizationViewModel? auditFinalizationVM = null)
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
        WorkingPapersVM = workingPapersVM;
        AuditChecklistVM = auditChecklistVM;
        InvestigationVM = investigationVM;
        AuditTrailVM = auditTrailVM;
        ManagementRepresentationLetterVM = managementRepresentationLetterVM;
        AuditEvidenceVM = auditEvidenceVM;
        AuditFinalizationVM = auditFinalizationVM;

        _currentViewModel = dashboardVM;

        _navigationService.Navigated += (s, sec) =>
        {
            if (CurrentSection != sec)
            {
                Navigate(sec);
            }
        };

        _companyContext.ActiveCompanyChanged += OnActiveCompanyChanged;

        _tallyConnection.StatusChanged += OnTallyStatusChanged;
        UpdateStatusDisplay(_tallyConnection.CurrentStatus);

        _ = RefreshActiveCompanyAsync();
    }

    private void OnActiveCompanyChanged(object? sender, Company? company)
    {
        Interlocked.Increment(ref _companyRefreshGeneration);
        var companyName = company?.TallyCompanyName
                          ?? _companyContext.TallyCompanyName
                          ?? _companyContext.ActiveCompanyName;

        void Update()
        {
            ActiveCompany = string.IsNullOrWhiteSpace(companyName)
                ? "No Company Selected"
                : companyName;
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

    public async Task RefreshActiveCompanyAsync()
    {
        var generation = Interlocked.Increment(ref _companyRefreshGeneration);
        var comp = await _companyContext.GetActiveCompanyAsync();

        if (generation != Volatile.Read(ref _companyRefreshGeneration))
        {
            return;
        }

        var current = _companyContext.CurrentCompany;
        var currentName = current?.TallyCompanyName
                          ?? _companyContext.TallyCompanyName
                          ?? _companyContext.ActiveCompanyName;

        var resolved = !string.IsNullOrWhiteSpace(currentName) ? currentName : comp?.TallyCompanyName;

        void Update()
        {
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                ActiveCompany = resolved;
            }
            else if (generation == Volatile.Read(ref _companyRefreshGeneration))
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
    }

    [RelayCommand]
    public void Navigate(string section)
    {
        var currentName = _companyContext.TallyCompanyName ?? _companyContext.ActiveCompanyName;
        if (!string.IsNullOrWhiteSpace(currentName))
        {
            ActiveCompany = currentName;
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
            "WorkingPapers" => WorkingPapersVM,
            "AuditChecklist" => AuditChecklistVM,
            "Settings" => SettingsVM,
            "AuditTrail" => AuditTrailVM,
            "ManagementLetter" => ManagementRepresentationLetterVM,
            "AuditEvidence" => AuditEvidenceVM,
            "AuditFinalization" => AuditFinalizationVM,
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
