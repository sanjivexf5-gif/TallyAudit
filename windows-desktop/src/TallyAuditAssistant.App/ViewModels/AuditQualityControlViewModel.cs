using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class AuditQualityControlViewModel : ObservableObject, INavigationAware
{
    private readonly IAuditQualityControlService _qualityControlService;
    private readonly IActiveCompanyContext _companyContext;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _companyName = "No Company Selected";
    [ObservableProperty] private string _financialYear = string.Empty;
    [ObservableProperty] private string _engagementStatus = "Uninitialized";
    [ObservableProperty] private int _passedChecks;
    [ObservableProperty] private int _warnings;
    [ObservableProperty] private int _attentionRequired;
    [ObservableProperty] private int _blocked;
    [ObservableProperty] private bool _readyForReview;
    [ObservableProperty] private bool _readyForFinalization;
    [ObservableProperty] private string _statusMessage = "Run quality control to review audit readiness.";

    public ObservableCollection<QualityControlCheckItem> Checks { get; } = new();

    public AuditQualityControlViewModel(
        IAuditQualityControlService qualityControlService,
        IActiveCompanyContext companyContext)
    {
        _qualityControlService = qualityControlService;
        _companyContext = companyContext;
    }

    public async Task OnNavigatedToAsync() => await RunChecksAsync();

    [RelayCommand]
    public async Task RunChecksAsync()
    {
        IsLoading = true;
        try
        {
            var company = await _companyContext.GetActiveCompanyAsync();
            if (company == null)
            {
                Checks.Clear();
                CompanyName = "No Company Selected";
                StatusMessage = "Select a TallyPrime company before running quality control.";
                return;
            }

            CompanyName = company.TallyCompanyName;
            var period = await _companyContext.GetActivePeriodAsync();
            var periodId = period?.FinancialPeriodId ?? $"FY-{company.BooksFromDate.Year}";
            FinancialYear = period?.FinancialYear ?? $"FY {company.BooksFromDate.Year}-{(company.BooksFromDate.Year + 1) % 100:D2}";

            var summary = await _qualityControlService.GetQualityControlSummaryAsync(company.Id, periodId);

            EngagementStatus = summary.EngagementStatus;
            PassedChecks = summary.PassedChecksCount;
            Warnings = summary.WarningsCount;
            AttentionRequired = summary.AttentionRequiredCount;
            Blocked = summary.BlockedCount;
            ReadyForReview = summary.IsReadyForReview;
            ReadyForFinalization = summary.IsReadyForFinalization;

            Checks.Clear();
            foreach (var check in summary.Checks)
                Checks.Add(check);

            StatusMessage = summary.IsReadyForFinalization
                ? "Quality control passed: the audit is ready for finalization."
                : summary.IsReadyForReview
                    ? "Core audit data is ready for review; outstanding QC items remain."
                    : "Quality control found items that require attention.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Quality control failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
