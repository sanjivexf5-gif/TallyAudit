using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class RiskDashboardViewModel : ObservableObject, INavigationAware
{
    private readonly IAuditRepository _repository;
    private readonly IAuditFinalizationRepository _finalizationRepository;
    private readonly IActiveCompanyContext _companyContext;

    [ObservableProperty] private string _activeCompanyName = "No Company Selected";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private int _totalFindings;
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private int _highCount;
    [ObservableProperty] private int _mediumCount;
    [ObservableProperty] private int _lowCount;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _reviewedCount;
    [ObservableProperty] private int _duplicateCount;
    [ObservableProperty] private int _gstCount;
    [ObservableProperty] private int _tdsCount;
    [ObservableProperty] private int _accountingCount;
    [ObservableProperty] private int _bankingCount;
    [ObservableProperty] private int _checklistCompleted;
    [ObservableProperty] private int _checklistApplicable;
    [ObservableProperty] private int _riskScore;
    [ObservableProperty] private string _riskLabel = "LOW";

    public ObservableCollection<RiskFindingItem> TopRisks { get; } = new();

    public int ChecklistPercent => ChecklistApplicable == 0 ? 0 : (int)Math.Round(ChecklistCompleted * 100.0 / ChecklistApplicable);

    public RiskDashboardViewModel(
        IAuditRepository repository,
        IAuditFinalizationRepository finalizationRepository,
        IActiveCompanyContext companyContext)
    {
        _repository = repository;
        _finalizationRepository = finalizationRepository;
        _companyContext = companyContext;
        _companyContext.ActiveCompanyChanged += OnActiveCompanyChanged;
        _ = LoadAsync();
    }

    public Task OnNavigatedToAsync() => LoadAsync();

    private void OnActiveCompanyChanged(object? sender, Company? company) => _ = LoadAsync();

    [RelayCommand]
    public Task RefreshAsync() => LoadAsync();

    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = string.Empty;
        try
        {
            var company = await _companyContext.GetActiveCompanyAsync();
            if (company == null)
            {
                Reset();
                StatusMessage = "Select and synchronize a company to populate the risk dashboard.";
                return;
            }

            ActiveCompanyName = company.TallyCompanyName;
            var findings = await _repository.GetExceptionsAsync(company.Id, take: 5000);
            TotalFindings = await _repository.GetExceptionCountAsync(company.Id);
            StatusMessage = TotalFindings > findings.Count
                ? $"Risk breakdown is based on {findings.Count:N0} loaded findings out of {TotalFindings:N0} total. Open Exceptions for a complete filtered review."
                : string.Empty;
            CriticalCount = findings.Count(x => x.Severity == SeverityLevel.Critical);
            HighCount = findings.Count(x => x.Severity == SeverityLevel.High);
            MediumCount = findings.Count(x => x.Severity == SeverityLevel.Medium);
            LowCount = findings.Count(x => x.Severity == SeverityLevel.Low);
            PendingCount = findings.Count(x => x.Status == ReviewStatus.Pending);
            ReviewedCount = findings.Count(x => x.Status == ReviewStatus.Reviewed || x.Status == ReviewStatus.Resolved);
            DuplicateCount = findings.Count(x => x.Category == RuleCategory.DuplicateDetection);
            GstCount = findings.Count(x => x.Category == RuleCategory.GST);
            TdsCount = findings.Count(x => x.Category == RuleCategory.TDS);
            AccountingCount = findings.Count(x => x.Category == RuleCategory.GeneralAccounting);
            BankingCount = findings.Count(x => x.Category == RuleCategory.Banking);

            RiskScore = Math.Min(100, CriticalCount * 30 + HighCount * 15 + MediumCount * 7 + LowCount * 3);
            RiskLabel = RiskScore >= 75 ? "CRITICAL" : RiskScore >= 50 ? "HIGH" : RiskScore >= 25 ? "MEDIUM" : "LOW";

            TopRisks.Clear();
            foreach (var finding in findings
                .OrderByDescending(x => SeverityWeight(x.Severity))
                .ThenByDescending(x => x.FlaggedAmount ?? 0m)
                .Take(10))
            {
                TopRisks.Add(new RiskFindingItem(
                    finding.Id,
                    finding.RuleName,
                    finding.Category.ToString(),
                    finding.Severity.ToString(),
                    finding.Status.ToString(),
                    finding.VoucherNumber ?? "—",
                    finding.LedgerName ?? "—"));
            }

            var checklist = await _finalizationRepository.GetChecklistAsync($"CHECKLIST-{company.Id}");
            ChecklistApplicable = checklist.Count(x => !string.Equals(x.Status, "Not Applicable", StringComparison.OrdinalIgnoreCase));
            ChecklistCompleted = checklist.Count(x => string.Equals(x.Status, "Completed", StringComparison.OrdinalIgnoreCase) && !string.Equals(x.Status, "Not Applicable", StringComparison.OrdinalIgnoreCase));
            OnPropertyChanged(nameof(ChecklistPercent));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to load risk dashboard: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Reset()
    {
        ActiveCompanyName = "No Company Selected";
        TotalFindings = CriticalCount = HighCount = MediumCount = LowCount = 0;
        PendingCount = ReviewedCount = DuplicateCount = GstCount = TdsCount = AccountingCount = BankingCount = 0;
        ChecklistCompleted = ChecklistApplicable = RiskScore = 0;
        RiskLabel = "LOW";
        TopRisks.Clear();
        OnPropertyChanged(nameof(ChecklistPercent));
    }

    private static int SeverityWeight(SeverityLevel severity) => severity switch
    {
        SeverityLevel.Critical => 4,
        SeverityLevel.High => 3,
        SeverityLevel.Medium => 2,
        _ => 1
    };
}

public sealed record RiskFindingItem(
    string Id,
    string RuleName,
    string Category,
    string Severity,
    string Status,
    string VoucherNumber,
    string LedgerName);
