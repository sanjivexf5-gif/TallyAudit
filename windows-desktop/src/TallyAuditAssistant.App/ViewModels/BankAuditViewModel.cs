using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class BankAuditViewModel : ObservableObject
{
    private readonly IAuditRepository _repository;
    private readonly ISettingsService _settingsService;
    private readonly IActiveCompanyContext _companyContext;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _activeCompanyName = string.Empty;

    [ObservableProperty]
    private AuditException? _selectedException;

    [ObservableProperty]
    private string _auditorNoteInput = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<AuditException> Exceptions { get; } = new();

    public BankAuditViewModel(
        IAuditRepository repository,
        ISettingsService settingsService,
        IActiveCompanyContext companyContext)
    {
        _repository = repository;
        _settingsService = settingsService;
        _companyContext = companyContext;

        _companyContext.ActiveCompanyChanged += OnActiveCompanyChanged;
        _ = LoadBankExceptionsAsync();
    }

    private void OnActiveCompanyChanged(object? sender, Company? comp)
    {
        _ = LoadBankExceptionsAsync();
    }

    public async Task LoadBankExceptionsAsync()
    {
        IsLoading = true;
        StatusMessage = string.Empty;
        try
        {
            var comp = await _companyContext.GetActiveCompanyAsync()
                       ?? await _companyContext.EnsureAndInitializeActiveCompanyAsync();
            var activeName = comp?.TallyCompanyName ?? await _settingsService.GetSettingAsync("ActiveCompany", string.Empty);
            ActiveCompanyName = activeName;

            var companies = await _repository.GetAllCompaniesAsync();
            if (companies.Count == 0)
            {
                Exceptions.Clear();
                return;
            }

            var current = (comp != null ? companies.FirstOrDefault(c => c.Id == comp.Id || c.TallyCompanyName == comp.TallyCompanyName) : null)
                          ?? (string.IsNullOrEmpty(activeName) ? companies[0] : (companies.FirstOrDefault(c => c.TallyCompanyName == activeName) ?? companies[0]));

            ActiveCompanyName = current.TallyCompanyName;

            var list = await _repository.GetExceptionsFilteredAsync(
                current.Id,
                category: "Banking",
                severity: "All",
                status: "All",
                searchQuery: null,
                sortBy: "Priority",
                isDescending: true
            );

            Exceptions.Clear();
            foreach (var ex in list)
            {
                Exceptions.Add(ex);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveExceptionStatusAsync()
    {
        if (SelectedException == null) return;

        try
        {
            await _repository.UpdateExceptionStatusAsync(SelectedException.Id, SelectedException.Status, AuditorNoteInput);
            StatusMessage = "Review status saved successfully.";
            await LoadBankExceptionsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task MarkAsReviewedAsync()
    {
        if (SelectedException == null) return;
        SelectedException.Status = ReviewStatus.Reviewed;
        await SaveExceptionStatusAsync();
    }

    [RelayCommand]
    private async Task MarkAsAcceptedAsync()
    {
        if (SelectedException == null) return;
        SelectedException.Status = ReviewStatus.Resolved;
        await SaveExceptionStatusAsync();
    }

    partial void OnSelectedExceptionChanged(AuditException? value)
    {
        if (value != null)
        {
            AuditorNoteInput = value.AuditorNote ?? string.Empty;
        }
    }
}
