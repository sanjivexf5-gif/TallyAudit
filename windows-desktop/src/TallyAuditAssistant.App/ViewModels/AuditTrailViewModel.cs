using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class AuditTrailViewModel : ObservableObject, INavigationAware
{
    private readonly IAuditTrailService _auditTrailService;
    private readonly IActiveCompanyContext _companyContext;

    [ObservableProperty]
    private ObservableCollection<AuditTrailEntry> _entries = new();

    [ObservableProperty]
    private AuditTrailEntry? _selectedEntry;

    [ObservableProperty]
    private bool _isDetailsOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private DateTime? _dateFrom;

    [ObservableProperty]
    private DateTime? _dateTo;

    [ObservableProperty]
    private string _selectedCompany = "All Companies";

    [ObservableProperty]
    private string _selectedModule = "All Modules";

    [ObservableProperty]
    private string _selectedAction = "All Actions";

    [ObservableProperty]
    private string _selectedUser = "All Users";

    [ObservableProperty]
    private ObservableCollection<string> _companies = new() { "All Companies" };

    [ObservableProperty]
    private ObservableCollection<string> _modules = new() { "All Modules" };

    [ObservableProperty]
    private ObservableCollection<string> _actions = new() { "All Actions" };

    [ObservableProperty]
    private ObservableCollection<string> _users = new() { "All Users" };

    [ObservableProperty]
    private int _totalRecordsCount;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public AuditTrailViewModel(
        IAuditTrailService auditTrailService,
        IActiveCompanyContext companyContext)
    {
        _auditTrailService = auditTrailService;
        _companyContext = companyContext;

        _companyContext.ActiveCompanyChanged += OnActiveCompanyChanged;
    }

    private void OnActiveCompanyChanged(object? sender, Company? company)
    {
        _ = RefreshAsync();
    }

    public async Task OnNavigatedToAsync()
    {
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading audit trail records...";
        try
        {
            var rawEntries = await _auditTrailService.GetEntriesAsync(
                searchTerm: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                fromUtc: DateFrom,
                toUtc: DateTo.HasValue ? DateTo.Value.Date.AddDays(1).AddTicks(-1) : null,
                companyName: SelectedCompany,
                module: SelectedModule,
                actionType: SelectedAction,
                userName: SelectedUser,
                limit: 1000,
                ct: CancellationToken.None);

            Entries.Clear();
            foreach (var item in rawEntries)
            {
                Entries.Add(item);
            }

            TotalRecordsCount = rawEntries.Count;
            UpdateFilterDropdowns(rawEntries);
            StatusMessage = $"Showing {TotalRecordsCount} audit trail record(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading audit trail: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateFilterDropdowns(IReadOnlyList<AuditTrailEntry> rawEntries)
    {
        var existingCompany = SelectedCompany;
        var existingModule = SelectedModule;
        var existingAction = SelectedAction;
        var existingUser = SelectedUser;

        var compList = rawEntries
            .Select(e => e.CompanyName)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        Companies.Clear();
        Companies.Add("All Companies");
        foreach (var c in compList) Companies.Add(c!);
        if (Companies.Contains(existingCompany)) SelectedCompany = existingCompany;

        var modList = rawEntries
            .Select(e => e.Module)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()
            .OrderBy(m => m)
            .ToList();

        Modules.Clear();
        Modules.Add("All Modules");
        foreach (var m in modList) Modules.Add(m);
        if (Modules.Contains(existingModule)) SelectedModule = existingModule;

        var actList = rawEntries
            .Select(e => e.ActionType)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct()
            .OrderBy(a => a)
            .ToList();

        Actions.Clear();
        Actions.Add("All Actions");
        foreach (var a in actList) Actions.Add(a);
        if (Actions.Contains(existingAction)) SelectedAction = existingAction;

        var userList = rawEntries
            .Select(e => e.UserName)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct()
            .OrderBy(u => u)
            .ToList();

        Users.Clear();
        Users.Add("All Users");
        foreach (var u in userList) Users.Add(u);
        if (Users.Contains(existingUser)) SelectedUser = existingUser;
    }

    [RelayCommand]
    public async Task ApplyFiltersAsync()
    {
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task ClearFiltersAsync()
    {
        SearchText = string.Empty;
        DateFrom = null;
        DateTo = null;
        SelectedCompany = "All Companies";
        SelectedModule = "All Modules";
        SelectedAction = "All Actions";
        SelectedUser = "All Users";

        await RefreshAsync();
    }

    [RelayCommand]
    public void ViewDetails(AuditTrailEntry? entry)
    {
        if (entry != null)
        {
            SelectedEntry = entry;
            IsDetailsOpen = true;
        }
    }

    [RelayCommand]
    public void CloseDetails()
    {
        IsDetailsOpen = false;
    }

    [RelayCommand]
    public async Task ExportExcelAsync()
    {
        try
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "CSV Spreadsheet (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName = $"AuditTrail_Export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv",
                Title = "Export Audit Trail to CSV/Excel"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var bytes = await _auditTrailService.ExportToExcelAsync(Entries.ToList());
                await File.WriteAllBytesAsync(saveDialog.FileName, bytes);
                StatusMessage = $"Audit Trail successfully exported to {Path.GetFileName(saveDialog.FileName)}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ExportPdfAsync()
    {
        try
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "Audit Trail Report (*.txt;*.pdf)|*.txt;*.pdf|All Files (*.*)|*.*",
                FileName = $"AuditTrail_Report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt",
                Title = "Export Audit Trail Report"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var bytes = await _auditTrailService.ExportToPdfAsync(Entries.ToList());
                await File.WriteAllBytesAsync(saveDialog.FileName, bytes);
                StatusMessage = $"Audit Trail report exported to {Path.GetFileName(saveDialog.FileName)}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }
}
