using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class TdsAuditViewModel : ObservableObject
{
    private readonly IAuditRepository _repository;
    private readonly ISettingsService _settingsService;

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

    public TdsAuditViewModel(IAuditRepository repository, ISettingsService settingsService)
    {
        _repository = repository;
        _settingsService = settingsService;
        _ = LoadTdsExceptionsAsync();
    }

    public async Task LoadTdsExceptionsAsync()
    {
        IsLoading = true;
        StatusMessage = string.Empty;
        try
        {
            var activeName = await _settingsService.GetSettingAsync("ActiveCompany", "Apex Industrial Solutions Pvt Ltd");
            ActiveCompanyName = activeName;

            var companies = await _repository.GetAllCompaniesAsync();
            if (companies.Count == 0) return;
            var current = companies.FirstOrDefault(c => c.TallyCompanyName == activeName) ?? companies[0];

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
