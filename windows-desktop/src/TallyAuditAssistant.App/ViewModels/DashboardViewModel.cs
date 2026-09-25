using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IAuditRepository _repository;
    private readonly ITallyConnection _tallyConnection;

    [ObservableProperty]
    private string _activeCompanyName = "Apex Industrial Solutions Pvt Ltd";

    [ObservableProperty]
    private string _financialYear = "FY 2025-26";

    [ObservableProperty]
    private int _gstExceptionsCount = 14;

    [ObservableProperty]
    private int _tdsExceptionsCount = 6;

    [ObservableProperty]
    private int _accountingExceptionsCount = 8;

    [ObservableProperty]
    private int _highPriorityCount = 9;

    [ObservableProperty]
    private int _pendingAuditorReviews = 18;

    [ObservableProperty]
    private int _auditCompletionPercentage = 68;

    [ObservableProperty]
    private int _totalVouchersSynchronized = 14250;

    [ObservableProperty]
    private string _lastSyncTime = "Today, 10:45 AM";

    [ObservableProperty]
    private bool _isLoading = false;

    public ObservableCollection<AuditException> RecentExceptions { get; } = new();

    public DashboardViewModel(IAuditRepository repository, ITallyConnection tallyConnection)
    {
        _repository = repository;
        _tallyConnection = tallyConnection;
        _ = LoadDashboardDataAsync();
    }

    [RelayCommand]
    private async Task RefreshDashboardAsync()
    {
        await LoadDashboardDataAsync();
    }

    private async Task LoadDashboardDataAsync()
    {
        IsLoading = true;
        try
        {
            var companies = await _repository.GetAllCompaniesAsync();
            if (companies.Count > 0)
            {
                var current = companies[0];
                ActiveCompanyName = current.TallyCompanyName;
                TotalVouchersSynchronized = await _repository.GetVoucherCountAsync(current.Id);
                LastSyncTime = current.LastSyncDate?.ToString("g") ?? "Never";

                var exceptions = await _repository.GetExceptionsAsync(current.Id, take: 10);
                RecentExceptions.Clear();
                foreach (var ex in exceptions)
                {
                    RecentExceptions.Add(ex);
                }

                GstExceptionsCount = await _repository.GetExceptionCountAsync(current.Id, category: RuleCategory.GST);
                TdsExceptionsCount = await _repository.GetExceptionCountAsync(current.Id, category: RuleCategory.TDS);
                AccountingExceptionsCount = await _repository.GetExceptionCountAsync(current.Id, category: RuleCategory.GeneralAccounting);
                HighPriorityCount = await _repository.GetExceptionCountAsync(current.Id, minSeverity: SeverityLevel.High);
                PendingAuditorReviews = await _repository.GetExceptionCountAsync(current.Id, status: ReviewStatus.Pending);
            }
        }
        catch
        {
            // Fail gracefully without crashing UI
        }
        finally
        {
            IsLoading = false;
        }
    }
}
