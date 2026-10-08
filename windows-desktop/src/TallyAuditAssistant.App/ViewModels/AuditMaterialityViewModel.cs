using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class AuditMaterialityViewModel : ObservableObject, INavigationAware
{
    private readonly IAuditFinalizationRepository _repository;
    private readonly IActiveCompanyContext _companyContext;
    private readonly IAuditTrailService _auditTrail;

    [ObservableProperty] private string _companyName = "No Company Selected";
    [ObservableProperty] private string _financialYear = string.Empty;
    [ObservableProperty] private decimal _benchmarkAmount;
    [ObservableProperty] private decimal _benchmarkPercentage = 5m;
    [ObservableProperty] private decimal _overallMateriality;
    [ObservableProperty] private decimal _performanceMateriality;
    [ObservableProperty] private decimal _trivialThreshold;
    [ObservableProperty] private int _populationCount;
    [ObservableProperty] private decimal _populationAmount;
    [ObservableProperty] private int _suggestedSampleSize;
    [ObservableProperty] private string _samplingMethod = "Risk-based";
    [ObservableProperty] private string _rationale = string.Empty;
    [ObservableProperty] private string _preparedBy = string.Empty;
    [ObservableProperty] private string _reviewerName = string.Empty;
    [ObservableProperty] private string _status = "Draft";
    [ObservableProperty] private string _statusMessage = "Set materiality and calculate a risk-based sample.";

    public ObservableCollection<string> SamplingMethods { get; } = new(new[] { "Risk-based", "Random", "Systematic", "100% testing" });

    public AuditMaterialityViewModel(IAuditFinalizationRepository repository, IActiveCompanyContext companyContext, IAuditTrailService auditTrail)
    {
        _repository = repository;
        _companyContext = companyContext;
        _auditTrail = auditTrail;
    }

    public async Task OnNavigatedToAsync() => await LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        var company = await _companyContext.GetActiveCompanyAsync();
        if (company == null)
        {
            CompanyName = "No Company Selected";
            return;
        }

        CompanyName = company.TallyCompanyName;
        var period = await _companyContext.GetActivePeriodAsync();
        FinancialYear = period?.FinancialYear ?? $"FY {company.BooksFromDate.Year}-{(company.BooksFromDate.Year + 1) % 100:D2}";
        var periodId = period?.FinancialPeriodId ?? $"FY-{company.BooksFromDate.Year}";

        var plan = await _repository.GetAuditMaterialityPlanAsync(company.Id, periodId);
        if (plan != null)
        {
            BenchmarkAmount = plan.BenchmarkAmount;
            BenchmarkPercentage = plan.BenchmarkPercentage;
            OverallMateriality = plan.OverallMateriality;
            PerformanceMateriality = plan.PerformanceMateriality;
            TrivialThreshold = plan.TrivialThreshold;
            PopulationCount = plan.PopulationCount;
            PopulationAmount = plan.PopulationAmount;
            SuggestedSampleSize = plan.SuggestedSampleSize;
            SamplingMethod = plan.SamplingMethod;
            Rationale = plan.Rationale;
            PreparedBy = plan.PreparedBy;
            ReviewerName = plan.ReviewerName;
            Status = plan.Status;
        }
        else
        {
            PopulationCount = await _repository.GetVoucherCountAsync(company.Id);
            PopulationAmount = await _repository.GetVoucherTotalAsync(company.Id);
            Calculate();
        }
    }

    partial void OnBenchmarkAmountChanged(decimal value) => Calculate();
    partial void OnBenchmarkPercentageChanged(decimal value) => Calculate();
    partial void OnPopulationCountChanged(int value) => Calculate();
    partial void OnPopulationAmountChanged(decimal value) => Calculate();
    partial void OnSamplingMethodChanged(string value) => Calculate();

    [RelayCommand]
    public void Calculate()
    {
        OverallMateriality = Math.Round(Math.Max(0, BenchmarkAmount) * Math.Clamp(BenchmarkPercentage, 0, 100) / 100m, 2);
        PerformanceMateriality = Math.Round(OverallMateriality * 0.75m, 2);
        TrivialThreshold = Math.Round(OverallMateriality * 0.05m, 2);

        if (SamplingMethod == "100% testing")
            SuggestedSampleSize = PopulationCount;
        else if (PopulationCount <= 0)
            SuggestedSampleSize = 0;
        else
        {
            var riskFactor = SamplingMethod == "Risk-based" ? 0.15m : SamplingMethod == "Systematic" ? 0.10m : 0.08m;
            var amountFactor = PerformanceMateriality > 0 && PopulationAmount > 0
                ? Math.Min(1m, PopulationAmount / (PerformanceMateriality * 20m))
                : 1m;
            SuggestedSampleSize = Math.Clamp((int)Math.Ceiling(PopulationCount * riskFactor * amountFactor), 1, Math.Min(PopulationCount, 100));
        }

        StatusMessage = $"Overall materiality: {OverallMateriality:N2} | Suggested sample: {SuggestedSampleSize:N0}";
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        var company = await _companyContext.GetActiveCompanyAsync();
        if (company == null) { StatusMessage = "Select a company first."; return; }

        var period = await _companyContext.GetActivePeriodAsync();
        var periodId = period?.FinancialPeriodId ?? $"FY-{company.BooksFromDate.Year}";
        var plan = new AuditMaterialityPlan
        {
            Id = $"MAT-{company.Id}-{periodId}",
            CompanyId = company.Id,
            FinancialPeriodId = periodId,
            BenchmarkAmount = BenchmarkAmount,
            BenchmarkPercentage = BenchmarkPercentage,
            OverallMateriality = OverallMateriality,
            PerformanceMateriality = PerformanceMateriality,
            TrivialThreshold = TrivialThreshold,
            PopulationCount = PopulationCount,
            PopulationAmount = PopulationAmount,
            SuggestedSampleSize = SuggestedSampleSize,
            SamplingMethod = SamplingMethod,
            Rationale = Rationale,
            PreparedBy = PreparedBy,
            ReviewerName = ReviewerName,
            Status = "Prepared",
            UpdatedAt = DateTime.UtcNow
        };
        await _repository.SaveAuditMaterialityPlanAsync(plan);
        Status = "Prepared";
        StatusMessage = "Materiality and sampling plan saved.";
        await _auditTrail.RecordActivityAsync("Materiality plan saved", "AUDIT PLANNING", $"Materiality {OverallMateriality:N2}; sample {SuggestedSampleSize:N0}.", company.TallyCompanyName, System.Threading.CancellationToken.None);
    }
}