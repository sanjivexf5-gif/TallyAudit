using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class AuditAutomationViewModel : ObservableObject, INavigationAware, IDisposable
{
    private readonly IAuditAutomationService _automationService;
    private readonly INavigationService _navigationService;
    private readonly IActiveCompanyContext _companyContext;
    private readonly AuditReportPackViewModel? _reportPackViewModel;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _companyName = "No Company Selected";
    [ObservableProperty] private string _stageTitle = "READY";
    [ObservableProperty] private string _statusMessage = "Click Start Automated Audit to run the complete audit workflow.";
    [ObservableProperty] private double _progressPercentage;
    [ObservableProperty] private int _findings;
    [ObservableProperty] private int _recordsSynchronized;
    [ObservableProperty] private string _elapsedTime = "00:00";
    [ObservableProperty] private bool _generateReport = true;
    [ObservableProperty] private bool _goToFindingsWhenComplete = true;

    private DateTime _startedAt;

    public AuditAutomationViewModel(
        IAuditAutomationService automationService,
        INavigationService navigationService,
        IActiveCompanyContext companyContext,
        AuditReportPackViewModel? reportPackViewModel = null)
    {
        _automationService = automationService;
        _navigationService = navigationService;
        _companyContext = companyContext;
        _reportPackViewModel = reportPackViewModel;

        _automationService.ProgressChanged += OnProgressChanged;
        _companyContext.ActiveCompanyChanged += OnCompanyChanged;

        RefreshCompany();
    }

    public Task OnNavigatedToAsync()
    {
        RefreshCompany();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task StartAutomatedAuditAsync()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        Findings = 0;
        RecordsSynchronized = 0;
        ProgressPercentage = 0;
        StageTitle = "STARTING";
        StatusMessage = "Preparing the automated audit workflow...";
        _startedAt = DateTime.Now;

        try
        {
            var result = await _automationService.RunAsync(
                runIncrementalSync: true,
                runFullAudit: true);

            CompanyName = string.IsNullOrWhiteSpace(result.CompanyName)
                ? CompanyName
                : result.CompanyName;

            RecordsSynchronized = result.RecordsSynchronized;
            Findings = result.FindingsGenerated;
            ElapsedTime = $"{(int)result.Duration.TotalMinutes:D2}:{result.Duration.Seconds:D2}";

            if (result.IsSuccess)
            {
                StatusMessage =
                    $"Completed automatically: {RecordsSynchronized:N0} records synchronized, " +
                    $"{Findings:N0} finding(s) generated.";

                // Generate the existing board-ready report pack automatically so
                // the auditor does not need a second workflow step.
                if (GenerateReport && _reportPackViewModel != null)
                {
                    StatusMessage += " Generating audit report pack...";
                    await _reportPackViewModel.GeneratePackAsync();
                    StatusMessage = $"Automation complete. {Findings:N0} finding(s) generated and report pack prepared.";
                }

                if (GoToFindingsWhenComplete)
                {
                    _navigationService.Navigate("Exceptions");
                }
            }
            else
            {
                StatusMessage = result.ErrorMessage ?? "Automation did not complete.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Automation failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        StatusMessage = "Stopping automation safely...";
        await _automationService.CancelAsync();
    }

    private void OnProgressChanged(object? sender, AuditAutomationProgress progress)
    {
        void Update()
        {
            StageTitle = progress.StageTitle;
            StatusMessage = progress.Message;
            ProgressPercentage = progress.Percentage;
            Findings = progress.Findings;
            ElapsedTime = _startedAt == default
                ? "00:00"
                : $"{(int)(DateTime.Now - _startedAt).TotalMinutes:D2}:{(DateTime.Now - _startedAt).Seconds:D2}";
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

    private void OnCompanyChanged(object? sender, TallyAuditAssistant.Core.Domain.Companies.Company? company)
    {
        RefreshCompany();
    }

    private void RefreshCompany()
    {
        var name = _companyContext.TallyCompanyName ?? _companyContext.ActiveCompanyName;
        if (!string.IsNullOrWhiteSpace(name))
        {
            CompanyName = name;
        }
    }

    public void Dispose()
    {
        _automationService.ProgressChanged -= OnProgressChanged;
        _companyContext.ActiveCompanyChanged -= OnCompanyChanged;
    }
}
