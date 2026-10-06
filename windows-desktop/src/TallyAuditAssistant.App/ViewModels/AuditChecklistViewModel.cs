using System;
using System.Collections.Generic;
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

public partial class AuditChecklistViewModel : ObservableObject, INavigationAware
{
    private readonly IAuditFinalizationRepository _repository;
    private readonly IActiveCompanyContext _companyContext;
    private readonly IAuditTrailService? _auditTrailService;
    private string _auditId = string.Empty;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _activeCompanyName = "No Company Selected";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private string _sectionFilter = "All";
    [ObservableProperty] private string _statusFilter = "All";
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private ChecklistItem? _selectedItem;

    public ObservableCollection<ChecklistItem> Items { get; } = new();

    public int TotalCount => Items.Count;
    public int CompletedCount => Items.Count(x => x.Status == "Completed");
    public int InProgressCount => Items.Count(x => x.Status == "In Progress");
    public int NotApplicableCount => Items.Count(x => x.Status == "Not Applicable");
    public int PendingCount => Items.Count(x => x.Status == "Not Started");
    public int ApplicableCount => Items.Count(x => x.Status != "Not Applicable");
    public int CompletionPercentage => ApplicableCount == 0 ? 100 : (int)Math.Round(CompletedCount * 100.0 / ApplicableCount);

    public IReadOnlyList<string> Sections { get; } = new[] { "All", "Planning", "Tally & Sync", "GST", "TDS", "Accounting", "Banking", "Findings", "Evidence", "Final Review" };
    public IReadOnlyList<string> Statuses { get; } = new[] { "All", "Not Started", "In Progress", "Completed", "Not Applicable" };

    private static readonly (string Section, string Code, string Description)[] DefaultProcedures =
    {
        ("Planning","PLAN-01","Confirm audit period and active company."),
        ("Planning","PLAN-02","Confirm opening balances and prior-period carry-forward are available."),
        ("Planning","PLAN-03","Review materiality and performance materiality assumptions."),
        ("Tally & Sync","SYNC-01","Confirm TallyPrime connection is read-only."),
        ("Tally & Sync","SYNC-02","Synchronize the required company data."),
        ("Tally & Sync","SYNC-03","Confirm synchronized voucher and master counts."),
        ("GST","GST-01","Review GST registration and company tax identifiers."),
        ("GST","GST-02","Review GST rate and tax-component inconsistencies."),
        ("GST","GST-03","Review input/output GST exception findings."),
        ("GST","GST-04","Review reverse-charge and place-of-supply exceptions."),
        ("TDS","TDS-01","Review TDS applicability by relevant sections."),
        ("TDS","TDS-02","Review threshold and deduction-rate exceptions."),
        ("TDS","TDS-03","Review PAN/non-PAN and higher-rate exception cases."),
        ("Accounting","ACC-01","Review unusual journal and round-figure entries."),
        ("Accounting","ACC-02","Review negative cash/bank and unusual balance exceptions."),
        ("Accounting","ACC-03","Review duplicate or candidate-duplicate transactions."),
        ("Accounting","ACC-04","Review ledger consistency and sequence anomalies."),
        ("Banking","BANK-01","Review bank-related audit exceptions."),
        ("Banking","BANK-02","Review unusual or high-value bank transactions."),
        ("Findings","FND-01","Review all critical and high-severity findings."),
        ("Findings","FND-02","Confirm each material finding has an auditor note."),
        ("Findings","FND-03","Link material findings to working papers where applicable."),
        ("Evidence","EVD-01","Confirm supporting evidence is attached for material procedures."),
        ("Evidence","EVD-02","Confirm evidence files are locally available and traceable."),
        ("Evidence","EVD-03","Confirm evidence hashes are retained for working-paper attachments."),
        ("Final Review","FIN-01","Confirm open high-risk items have a documented disposition."),
        ("Final Review","FIN-02","Confirm working papers are complete and reviewed."),
        ("Final Review","FIN-03","Confirm automated audit report has been generated."),
        ("Final Review","FIN-04","Document the overall audit conclusion and limitations.")
    };

    public AuditChecklistViewModel(IAuditFinalizationRepository repository, IActiveCompanyContext companyContext, IAuditTrailService? auditTrailService = null)
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
                ActiveCompanyName = "No Company Selected"; Items.Clear(); _auditId = string.Empty;
                NotifyCounts(); return;
            }

            ActiveCompanyName = company.TallyCompanyName;
            _auditId = $"CHECKLIST-{company.Id}";
            var existing = (await _repository.GetChecklistAsync(_auditId)).ToList();

            if (existing.Count == 0)
            {
                foreach (var procedure in DefaultProcedures)
                {
                    await _repository.SaveChecklistItemAsync(new ChecklistItem
                    {
                        AuditId = _auditId,
                        Section = procedure.Section,
                        Code = procedure.Code,
                        Description = procedure.Description,
                        Status = "Not Started",
                        IsCompleted = false
                    });
                }
                existing = (await _repository.GetChecklistAsync(_auditId)).ToList();
            }

            var filtered = existing
                .Where(x => SectionFilter == "All" || x.Section == SectionFilter)
                .Where(x => StatusFilter == "All" || x.Status == StatusFilter)
                .Where(x => string.IsNullOrWhiteSpace(SearchQuery) ||
                            x.Code.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                            x.Description.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => Array.IndexOf(Sections.ToArray(), x.Section))
                .ThenBy(x => x.Code)
                .ToList();

            Items.Clear();
            foreach (var item in filtered) Items.Add(item);
            SelectedItem = Items.FirstOrDefault();
            NotifyCounts();
            StatusMessage = $"{Items.Count} checklist procedure(s) shown.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to load checklist: {ex.Message}";
            IsError = true;
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task SaveItemAsync(ChecklistItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(_auditId)) return;
        IsLoading = true; IsError = false;
        try
        {
            item.AuditId = _auditId;
            item.IsCompleted = item.Status == "Completed";
            item.CompletedAt = item.IsCompleted ? (item.CompletedAt ?? DateTime.UtcNow) : null;
            item.CompletedBy = item.IsCompleted ? Environment.UserName : string.Empty;
            await _repository.SaveChecklistItemAsync(item);

            if (_auditTrailService != null)
                await _auditTrailService.RecordActivityAsync(
                    actionType: "Audit checklist procedure updated",
                    module: "AUDIT CHECKLIST",
                    description: $"{item.Code}: {item.Status} — {item.Description}",
                    companyName: ActiveCompanyName,
                    ct: CancellationToken.None);

            NotifyCounts();
            StatusMessage = $"{item.Code} saved as {item.Status}.";
        }
        catch (Exception ex) { StatusMessage = $"Save failed: {ex.Message}"; IsError = true; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task CompleteItemAsync(ChecklistItem? item)
    {
        if (item == null) return;
        item.Status = "Completed";
        await SaveItemAsync(item);
    }

    [RelayCommand]
    public async Task ResetItemAsync(ChecklistItem? item)
    {
        if (item == null) return;
        item.Status = "Not Started";
        await SaveItemAsync(item);
    }

    partial void OnSectionFilterChanged(string value) => _ = LoadAsync();
    partial void OnStatusFilterChanged(string value) => _ = LoadAsync();
    partial void OnSearchQueryChanged(string value) => _ = LoadAsync();

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(InProgressCount));
        OnPropertyChanged(nameof(NotApplicableCount));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(ApplicableCount));
        OnPropertyChanged(nameof(CompletionPercentage));
    }
}
