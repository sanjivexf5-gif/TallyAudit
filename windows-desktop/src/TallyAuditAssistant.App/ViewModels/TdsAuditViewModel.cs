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

public partial class TdsAuditViewModel : ObservableObject, INavigationAware
{
    private readonly IAuditRepository _repository;
    private readonly ISettingsService _settingsService;
    private readonly IActiveCompanyContext _companyContext;
    private readonly INavigationService _navigationService;

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

    [ObservableProperty]
    private bool _hasExceptions;

    public ObservableCollection<AuditException> Exceptions { get; } = new();

    public TdsAuditViewModel(
        IAuditRepository repository,
        ISettingsService settingsService,
        IActiveCompanyContext companyContext,
        INavigationService navigationService)
    {
        _repository = repository;
        _settingsService = settingsService;
        _companyContext = companyContext;
        _navigationService = navigationService;

        _companyContext.ActiveCompanyChanged += OnActiveCompanyChanged;
        _ = LoadTdsExceptionsAsync();
    }

    public async Task OnNavigatedToAsync()
    {
        await LoadTdsExceptionsAsync();
    }

    private void OnActiveCompanyChanged(object? sender, Company? comp)
    {
        _ = LoadTdsExceptionsAsync();
    }

    [RelayCommand]
    public async Task LoadTdsExceptionsAsync()
    {
        IsLoading = true;
        StatusMessage = string.Empty;
        try
        {
            var comp = await _companyContext.GetActiveCompanyAsync();
            if (comp == null)
            {
                ActiveCompanyName = "No Company Selected";
                Exceptions.Clear();
                HasExceptions = false;
                return;
            }

            var current = comp;
            ActiveCompanyName = current.TallyCompanyName;

            var list = await _repository.GetExceptionsFilteredAsync(
                current.Id,
                category: "TDS",
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
            HasExceptions = Exceptions.Count > 0;
            if (SelectedException == null && Exceptions.Count > 0)
            {
                SelectedException = Exceptions[0];
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void RunComprehensiveAudit()
    {
        _navigationService.Navigate("Dashboard");
    }

    [RelayCommand]
    public void GoToSync()
    {
        _navigationService.Navigate("Sync");
    }

    [RelayCommand]
    private async Task SaveExceptionStatusAsync()
    {
        if (SelectedException == null) return;

        try
        {
            await _repository.UpdateExceptionStatusAsync(SelectedException.Id, SelectedException.Status, AuditorNoteInput);
            StatusMessage = "Review status saved successfully.";
            await LoadTdsExceptionsAsync();
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
