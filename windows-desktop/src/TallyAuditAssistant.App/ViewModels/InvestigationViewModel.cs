using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class InvestigationViewModel : ObservableObject, INavigationAware
{
    private readonly IInvestigationService _investigationService;
    private readonly IAuditRepository _auditRepository;
    private readonly IActiveCompanyContext _companyContext;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _activeCompanyName = string.Empty;

    [ObservableProperty]
    private AuditException? _targetException;

    [ObservableProperty]
    private ExceptionInvestigation? _investigation;

    [ObservableProperty]
    private InvestigationRelatedData? _relatedData;

    [ObservableProperty]
    private InvestigationStatus _currentStatus;

    [ObservableProperty]
    private RootCauseClassification _currentRootCause;

    [ObservableProperty]
    private InvestigationConclusion _currentConclusion;

    [ObservableProperty]
    private string _conclusionNotes = string.Empty;

    [ObservableProperty]
    private string _auditorNotes = string.Empty;

    [ObservableProperty]
    private string _managementResponse = string.Empty;

    [ObservableProperty]
    private string _proposedCorrectiveAction = string.Empty;

    [ObservableProperty]
    private string _reviewerNotes = string.Empty;

    [ObservableProperty]
    private string _activeTab = "Checklist";

    public ObservableCollection<InvestigationChecklistItem> ChecklistItems { get; } = new();
    public ObservableCollection<InvestigationStatus> AllowedTransitions { get; } = new();
    public Array AvailableConclusions => Enum.GetValues(typeof(InvestigationConclusion));

    public InvestigationViewModel(
        IInvestigationService investigationService,
        IAuditRepository auditRepository,
        IActiveCompanyContext companyContext,
        INavigationService navigationService)
    {
        _investigationService = investigationService;
        _auditRepository = auditRepository;
        _companyContext = companyContext;
        _navigationService = navigationService;
    }

    public async Task OnNavigatedToAsync()
    {
        var comp = await _companyContext.GetActiveCompanyAsync();
        if (comp != null)
        {
            ActiveCompanyName = comp.TallyCompanyName;
            // If no exception loaded, load first available exception
            if (TargetException == null)
            {
                var exceptions = await _auditRepository.GetExceptionsAsync(comp.Id, take: 1);
                if (exceptions.Count > 0)
                {
                    await LoadForExceptionAsync(exceptions[0]);
                }
            }
        }
    }

    [RelayCommand]
    public async Task LoadForExceptionAsync(AuditException exception)
    {
        IsLoading = true;
        StatusMessage = string.Empty;
        try
        {
            TargetException = exception;
            var comp = await _companyContext.GetActiveCompanyAsync();
            var companyId = comp?.Id ?? exception.CompanyId;

            var inv = await _investigationService.GetOrCreateInvestigationAsync(
                exception.Id,
                companyId,
                "Auditor",
                ct: default
            );

            Investigation = inv;
            CurrentStatus = inv.Status;
            CurrentRootCause = inv.RootCause;
            CurrentConclusion = inv.Conclusion;
            ConclusionNotes = inv.ConclusionNotes ?? string.Empty;
            AuditorNotes = inv.AuditorNotes ?? string.Empty;
            ManagementResponse = inv.ManagementResponse ?? string.Empty;
            ProposedCorrectiveAction = inv.ProposedCorrectiveAction ?? string.Empty;
            ReviewerNotes = inv.ReviewerNotes ?? string.Empty;

            ChecklistItems.Clear();
            foreach (var item in inv.ChecklistItems)
            {
                ChecklistItems.Add(item);
            }

            UpdateAllowedTransitions();

            RelatedData = await _investigationService.GetRelatedDataAsync(exception.Id, companyId);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading investigation: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SaveInvestigationAsync()
    {
        if (Investigation == null) return;

        IsLoading = true;
        StatusMessage = string.Empty;
        try
        {
            Investigation.RootCause = CurrentRootCause;
            Investigation.Conclusion = CurrentConclusion;
            Investigation.ConclusionNotes = ConclusionNotes;
            Investigation.AuditorNotes = AuditorNotes;
            Investigation.ManagementResponse = ManagementResponse;
            Investigation.ProposedCorrectiveAction = ProposedCorrectiveAction;
            Investigation.ReviewerNotes = ReviewerNotes;

            await _investigationService.UpdateInvestigationAsync(Investigation, "Auditor");
            StatusMessage = "Investigation successfully saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SaveConclusionAsync()
    {
        if (Investigation == null) return;

        IsLoading = true;
        StatusMessage = string.Empty;
        try
        {
            await _investigationService.SaveConclusionAsync(
                Investigation.Id,
                CurrentConclusion,
                ConclusionNotes,
                "Auditor"
            );
            Investigation.Conclusion = CurrentConclusion;
            Investigation.ConclusionNotes = ConclusionNotes;
            StatusMessage = $"Auditor conclusion set to '{CurrentConclusion}'. Saved successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save conclusion: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task TransitionToAsync(InvestigationStatus newStatus)
    {
        if (Investigation == null) return;

        IsLoading = true;
        StatusMessage = string.Empty;
        try
        {
            await _investigationService.TransitionStatusAsync(Investigation.Id, newStatus, "Auditor");
            CurrentStatus = newStatus;
            Investigation.Status = newStatus;
            UpdateAllowedTransitions();
            StatusMessage = $"Status changed to {newStatus}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Transition failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ToggleChecklistItemAsync(InvestigationChecklistItem item)
    {
        try
        {
            item.IsCompleted = !item.IsCompleted;
            await _investigationService.ToggleChecklistItemAsync(item.Id, item.IsCompleted, "Auditor", item.Notes);
            StatusMessage = $"Checklist item '{item.Description}' updated.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to update checklist item: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ReturnToExceptions()
    {
        _navigationService.Navigate("Exceptions");
    }

    private void UpdateAllowedTransitions()
    {
        AllowedTransitions.Clear();
        var allowed = _investigationService.GetAllowedTransitions(CurrentStatus);
        foreach (var status in allowed)
        {
            AllowedTransitions.Add(status);
        }
    }
}
