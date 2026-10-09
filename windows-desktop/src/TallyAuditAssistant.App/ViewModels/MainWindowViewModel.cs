using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
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

    // View models are resolved on first navigation, not all at application startup.
    // This avoids running database loads and keeping every screen's data in memory
    // when the user is only using a small part of the application.
    public DashboardViewModel DashboardVM => _services.GetRequiredService<DashboardViewModel>();
    public TallyConnectionViewModel ConnectionVM => _services.GetRequiredService<TallyConnectionViewModel>();
    public SyncViewModel SyncVM => _services.GetRequiredService<SyncViewModel>();
    public AuditAutomationViewModel AuditAutomationVM => _services.GetRequiredService<AuditAutomationViewModel>();
    public SettingsViewModel SettingsVM => _services.GetRequiredService<SettingsViewModel>();
    public CompaniesViewModel CompaniesVM => _services.GetRequiredService<CompaniesViewModel>();
    public GstAuditViewModel GstVM => _services.GetRequiredService<GstAuditViewModel>();
    public TdsAuditViewModel TdsVM => _services.GetRequiredService<TdsAuditViewModel>();
    public VouchersViewModel VouchersVM => _services.GetRequiredService<VouchersViewModel>();
    public LedgersViewModel LedgersVM => _services.GetRequiredService<LedgersViewModel>();
    public BankAuditViewModel BankVM => _services.GetRequiredService<BankAuditViewModel>();
    public ExceptionsViewModel ExceptionsVM => _services.GetRequiredService<ExceptionsViewModel>();
    public ReportsViewModel ReportsVM => _services.GetRequiredService<ReportsViewModel>();
    public WorkingPapersViewModel WorkingPapersVM => _services.GetRequiredService<WorkingPapersViewModel>();
    public AuditChecklistViewModel AuditChecklistVM => _services.GetRequiredService<AuditChecklistViewModel>();
    public InvestigationViewModel InvestigationVM => _services.GetRequiredService<InvestigationViewModel>();
    public AuditTrailViewModel AuditTrailVM => _services.GetRequiredService<AuditTrailViewModel>();
    public ManagementRepresentationLetterViewModel? ManagementRepresentationLetterVM => _services.GetRequiredService<ManagementRepresentationLetterViewModel>();
    public AuditEvidenceViewModel? AuditEvidenceVM => _services.GetRequiredService<AuditEvidenceViewModel>();
    public AuditFinalizationViewModel? AuditFinalizationVM => _services.GetRequiredService<AuditFinalizationViewModel>();
    public AuditReportPackViewModel? AuditReportPackVM => _services.GetRequiredService<AuditReportPackViewModel>();
    public AuditQueriesViewModel? AuditQueriesVM => _services.GetRequiredService<AuditQueriesViewModel>();
    public AuditQualityControlViewModel? AuditQualityControlVM => _services.GetRequiredService<AuditQualityControlViewModel>();
    public AuditMaterialityViewModel? AuditMaterialityVM => _services.GetRequiredService<AuditMaterialityViewModel>();

    public MainWindowViewModel(
        IServiceProvider services,
        ITallyConnection tallyConnection,
        IActiveCompanyContext companyContext,
        ITallyCompanyService companyService,
        ISettingsService settingsService,
        INavigationService navigationService)
    {
        _services = services;
        _tallyConnection = tallyConnection;
        _companyContext = companyContext;
        _companyService = companyService;
        _settingsService = settingsService;
        _navigationService = navigationService;

        _currentViewModel = DashboardVM;

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

    private static async Task NotifyNavigationAsync(INavigationAware navAware, string section)
    {
        try
        {
            // Keep navigation lifecycle on the WPF dispatcher context.
            // Running this on Task.Run can update bound properties from a
            // worker thread and cause runtime exceptions when opening views.
            await navAware.OnNavigatedToAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error on navigating to {section}: {ex}");
        }
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
            "Automation" => AuditAutomationVM,
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
            "AuditReportPack" => AuditReportPackVM,
            "AuditQueries" => AuditQueriesVM,
            "AuditQualityControl" => AuditQualityControlVM,
            "AuditMateriality" => AuditMaterialityVM,
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
                _ = NotifyNavigationAsync(navAware, section);
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
