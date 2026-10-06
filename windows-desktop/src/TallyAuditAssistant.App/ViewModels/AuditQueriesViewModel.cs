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

public partial class AuditQueriesViewModel : ObservableObject, INavigationAware
{
    private readonly IAuditFinalizationRepository _repository;
    private readonly IActiveCompanyContext _companyContext;
    private readonly IAuditTrailService? _auditTrailService;
    private string _planId = string.Empty;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _activeCompanyName = "No Company Selected";
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _statusFilter = "All";
    [ObservableProperty] private string _priorityFilter = "All";
    [ObservableProperty] private AuditQuery? _selectedQuery;
    [ObservableProperty] private string _queryNumberInput = string.Empty;
    [ObservableProperty] private string _titleInput = string.Empty;
    [ObservableProperty] private string _detailsInput = string.Empty;
    [ObservableProperty] private string _auditAreaInput = "General";
    [ObservableProperty] private string _findingIdInput = string.Empty;
    [ObservableProperty] private string _responsiblePersonInput = string.Empty;
    [ObservableProperty] private string _priorityInput = "Medium";
    [ObservableProperty] private string _statusInput = "Open";
    [ObservableProperty] private DateTime? _dueDateInput;
    [ObservableProperty] private string _managementResponseInput = string.Empty;
    [ObservableProperty] private string _auditorRemarksInput = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isSuccess;

    public ObservableCollection<AuditQuery> Queries { get; } = new();
    public int TotalCount => Queries.Count;
    public int OpenCount => Queries.Count(x => x.Status == "Open" || x.Status == "Sent" || x.Status == "Under Review");
    public int AwaitingResponseCount => Queries.Count(x => x.Status == "Sent");
    public int OverdueCount => Queries.Count(x => x.DueDate.HasValue && x.DueDate.Value.Date < DateTime.Today && x.Status != "Closed" && x.Status != "Not Applicable");
    public int ClosedCount => Queries.Count(x => x.Status == "Closed" || x.Status == "Not Applicable");

    public AuditQueriesViewModel(IAuditFinalizationRepository repository, IActiveCompanyContext companyContext, IAuditTrailService? auditTrailService = null)
    {
        _repository = repository;
        _companyContext = companyContext;
        _auditTrailService = auditTrailService;
        _companyContext.ActiveCompanyChanged += OnActiveCompanyChanged;
        _ = LoadAsync();
    }

    public async Task OnNavigatedToAsync() => await LoadAsync();
    private void OnActiveCompanyChanged(object? sender, Company? company) => _ = LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true; IsError = false;
        try
        {
            var company = await _companyContext.GetActiveCompanyAsync();
            if (company == null)
            {
                ActiveCompanyName = "No Company Selected"; _planId = string.Empty; Queries.Clear(); NotifyCounts(); return;
            }

            ActiveCompanyName = company.TallyCompanyName;
            _planId = await _repository.GetOrCreateWorkingPaperPlanIdAsync(company.Id, company.TallyCompanyName);
            var items = await _repository.GetAuditQueriesAsync(_planId);
            var filtered = items.Where(x =>
                (string.IsNullOrWhiteSpace(SearchQuery) ||
                 x.QueryNumber.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                 x.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                 x.Details.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                 x.ResponsiblePerson.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) &&
                (StatusFilter == "All" || x.Status == StatusFilter) &&
                (PriorityFilter == "All" || x.Priority == PriorityFilter)).ToList();

            Queries.Clear();
            foreach (var item in filtered) Queries.Add(item);
            NotifyCounts();
            StatusMessage = $"{Queries.Count} audit query(ies) available.";
        }
        catch (Exception ex) { StatusMessage = $"Unable to load audit queries: {ex.Message}"; IsError = true; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public void NewQuery()
    {
        SelectedQuery = null;
        QueryNumberInput = string.Empty; TitleInput = string.Empty; DetailsInput = string.Empty;
        AuditAreaInput = "General"; FindingIdInput = string.Empty; ResponsiblePersonInput = string.Empty;
        PriorityInput = "Medium"; StatusInput = "Open"; DueDateInput = DateTime.Today.AddDays(7);
        ManagementResponseInput = string.Empty; AuditorRemarksInput = string.Empty;
        StatusMessage = "New audit query ready.";
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_planId)) { StatusMessage = "Select and synchronize a company first."; IsError = true; return; }
        if (string.IsNullOrWhiteSpace(TitleInput)) { StatusMessage = "Query title is required."; IsError = true; return; }
        if (string.IsNullOrWhiteSpace(DetailsInput)) { StatusMessage = "Query details are required."; IsError = true; return; }

        IsLoading = true; IsError = false; IsSuccess = false;
        try
        {
            var company = await _companyContext.GetActiveCompanyAsync();
            if (company == null) return;
            var query = SelectedQuery ?? new AuditQuery();
            query.PlanId = _planId;
            query.QueryNumber = string.IsNullOrWhiteSpace(QueryNumberInput) ? $"AQ-{DateTime.Now:yyyyMMdd-HHmmss}" : QueryNumberInput.Trim();
            query.Title = TitleInput.Trim();
            query.Details = DetailsInput.Trim();
            query.AuditArea = string.IsNullOrWhiteSpace(AuditAreaInput) ? "General" : AuditAreaInput.Trim();
            query.FindingId = string.IsNullOrWhiteSpace(FindingIdInput) ? null : FindingIdInput.Trim();
            query.ResponsiblePerson = ResponsiblePersonInput.Trim();
            query.Priority = PriorityInput;
            query.Status = StatusInput;
            query.DueDate = DueDateInput;
            query.ResponseDate = string.IsNullOrWhiteSpace(ManagementResponseInput) ? null : (query.ResponseDate ?? DateTime.UtcNow);
            query.ManagementResponse = ManagementResponseInput.Trim();
            query.AuditorRemarks = AuditorRemarksInput.Trim();
            query.UpdatedAt = DateTime.UtcNow;

            await _repository.SaveAuditQueryAsync(query);
            if (_auditTrailService != null)
                await _auditTrailService.RecordActivityAsync(
                    actionType: "Audit query saved", module: "AUDIT QUERIES",
                    description: $"Audit query '{query.QueryNumber}' saved with status '{query.Status}'.",
                    companyName: company.TallyCompanyName, ct: CancellationToken.None);

            await LoadAsync();
            SelectedQuery = Queries.FirstOrDefault(x => x.Id == query.Id);
            StatusMessage = "Audit query saved successfully."; IsSuccess = true;
        }
        catch (Exception ex) { StatusMessage = $"Save failed: {ex.Message}"; IsError = true; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task SetStatusAsync(string status)
    {
        if (SelectedQuery == null) return;
        StatusInput = status;
        await SaveAsync();
    }

    partial void OnSelectedQueryChanged(AuditQuery? value)
    {
        if (value == null)
        {
            QueryNumberInput = string.Empty; TitleInput = string.Empty; DetailsInput = string.Empty;
            AuditAreaInput = "General"; FindingIdInput = string.Empty; ResponsiblePersonInput = string.Empty;
            PriorityInput = "Medium"; StatusInput = "Open"; DueDateInput = null;
            ManagementResponseInput = string.Empty; AuditorRemarksInput = string.Empty; return;
        }
        QueryNumberInput = value.QueryNumber; TitleInput = value.Title; DetailsInput = value.Details;
        AuditAreaInput = value.AuditArea; FindingIdInput = value.FindingId ?? string.Empty;
        ResponsiblePersonInput = value.ResponsiblePerson; PriorityInput = value.Priority; StatusInput = value.Status;
        DueDateInput = value.DueDate; ManagementResponseInput = value.ManagementResponse;
        AuditorRemarksInput = value.AuditorRemarks;
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(TotalCount)); OnPropertyChanged(nameof(OpenCount));
        OnPropertyChanged(nameof(AwaitingResponseCount)); OnPropertyChanged(nameof(OverdueCount));
        OnPropertyChanged(nameof(ClosedCount));
    }
}
