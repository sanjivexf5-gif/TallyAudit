using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class ExceptionsViewModel : ObservableObject, INavigationAware, IDisposable
{
    private readonly IAuditRepository _repository;
    private readonly ISettingsService _settingsService;
    private readonly IActiveCompanyContext _companyContext;
    private readonly INavigationService _navigationService;
    private readonly InvestigationViewModel? _investigationViewModel;
    private readonly ILogger<ExceptionsViewModel> _logger;

    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly object _ctsLock = new();
    private CancellationTokenSource? _loadCts;
    private long _loadGeneration;
    private bool _isDisposed;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _activeCompanyName = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedCategoryFilter = "All";

    [ObservableProperty]
    private string _selectedSeverityFilter = "All";

    [ObservableProperty]
    private string _selectedStatusFilter = "All";

    [ObservableProperty]
    private string _selectedSortColumn = "Date";

    [ObservableProperty]
    private bool _isSortDescending = true;

    [ObservableProperty]
    private AuditException? _selectedException;

    [ObservableProperty]
    private string _auditorNoteInput = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasExceptions;

    public ObservableCollection<AuditException> Exceptions { get; } = new();

    public ExceptionsViewModel(
        IAuditRepository repository,
        ISettingsService settingsService,
        IActiveCompanyContext companyContext,
        INavigationService navigationService,
        InvestigationViewModel? investigationViewModel = null,
        ILogger<ExceptionsViewModel>? logger = null)
    {
        _repository = repository;
        _settingsService = settingsService;
        _companyContext = companyContext;
        _navigationService = navigationService;
        _investigationViewModel = investigationViewModel;
        _logger = logger ?? NullLogger<ExceptionsViewModel>.Instance;

        _companyContext.ActiveCompanyChanged += OnActiveCompanyChanged;
        _ = SafeLoadExceptionsAsync();
    }

    public async Task OnNavigatedToAsync()
    {
        await SafeLoadExceptionsAsync();
    }

    private void OnActiveCompanyChanged(object? sender, Company? comp)
    {
        _ = SafeLoadExceptionsAsync();
    }

    private async Task SafeLoadExceptionsAsync()
    {
        try
        {
            await LoadExceptionsAsync();
        }
        catch (OperationCanceledException)
        {
            // Expected on filter change / fast succession
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in SafeLoadExceptionsAsync background trigger.");
        }
    }

    [RelayCommand]
    public async Task LoadExceptionsAsync()
    {
        if (_isDisposed) return;

        long currentGen = Interlocked.Increment(ref _loadGeneration);
        CancellationToken token;

        lock (_ctsLock)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
            token = _loadCts.Token;
        }

        _logger.LogInformation("Exceptions load started. Generation: {Generation}", currentGen);

        try
        {
            await _loadGate.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Exceptions load gate wait cancelled. Generation: {Generation}", currentGen);
            return;
        }

        try
        {
            if (token.IsCancellationRequested || currentGen != Interlocked.Read(ref _loadGeneration))
            {
                _logger.LogInformation("Exceptions load superseded before execution. Generation: {Generation} (Current: {CurrentGen})", currentGen, Interlocked.Read(ref _loadGeneration));
                return;
            }

            IsLoading = true;
            StatusMessage = string.Empty;

            var comp = await _companyContext.GetActiveCompanyAsync(token);
            if (token.IsCancellationRequested || currentGen != Interlocked.Read(ref _loadGeneration))
            {
                _logger.LogInformation("Exceptions load superseded after company fetch. Generation: {Generation}", currentGen);
                return;
            }

            if (comp == null)
            {
                _logger.LogInformation("No active company found for exceptions. Generation: {Generation}", currentGen);
                ActiveCompanyName = "No Company Selected";
                SelectedException = null;
                Exceptions.Clear();
                HasExceptions = false;
                return;
            }

            string compId = comp.Id;
            string compName = comp.TallyCompanyName;

            _logger.LogInformation("Fetching exceptions from repository. Generation: {Generation}, CompanyId: {CompanyId}", currentGen, compId);

            IReadOnlyList<AuditException> list = await _repository.GetExceptionsFilteredAsync(
                compId,
                SelectedCategoryFilter,
                SelectedSeverityFilter,
                SelectedStatusFilter,
                SearchQuery,
                SelectedSortColumn,
                IsSortDescending,
                token
            );

            if (token.IsCancellationRequested || currentGen != Interlocked.Read(ref _loadGeneration))
            {
                _logger.LogInformation("Exceptions load superseded after repository query. Generation: {Generation} (Current: {CurrentGen})", currentGen, Interlocked.Read(ref _loadGeneration));
                return;
            }

            _logger.LogInformation("Exceptions fetched. Generation: {Generation}, Count: {Count}", currentGen, list.Count);

            // Mutate UI ObservableCollection safely on current generation
            ActiveCompanyName = compName;
            SelectedException = null;
            Exceptions.Clear();

            foreach (var ex in list)
            {
                Exceptions.Add(ex);
            }

            HasExceptions = Exceptions.Count > 0;
            if (Exceptions.Count > 0)
            {
                var existingId = SelectedException?.Id;
                var matched = existingId != null ? Exceptions.FirstOrDefault(e => e.Id == existingId) : null;
                SelectedException = matched ?? Exceptions[0];
            }
            else
            {
                SelectedException = null;
                HasExceptions = false;
            }

            _logger.LogDebug("Exceptions load completed. Generation: {Generation}", currentGen);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Exceptions load cancelled during execution. Generation: {Generation}", currentGen);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading exceptions for generation {Generation}", currentGen);
            StatusMessage = $"Failed to load findings: {ex.Message}";
        }
        finally
        {
            if (currentGen == Interlocked.Read(ref _loadGeneration))
            {
                IsLoading = false;
            }
            _loadGate.Release();
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
            await SafeLoadExceptionsAsync();
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

    [RelayCommand]
    public async Task OpenInvestigationAsync()
    {
        if (SelectedException == null) return;

        if (_investigationViewModel != null)
        {
            await _investigationViewModel.LoadForExceptionAsync(SelectedException);
        }
        _navigationService.Navigate("Investigation");
    }

    partial void OnSelectedExceptionChanged(AuditException? value)
    {
        if (value != null)
        {
            AuditorNoteInput = value.AuditorNote ?? string.Empty;
        }
        else
        {
            AuditorNoteInput = string.Empty;
        }
    }

    async partial void OnSearchQueryChanged(string value) => await SafeLoadExceptionsAsync();
    async partial void OnSelectedCategoryFilterChanged(string value) => await SafeLoadExceptionsAsync();
    async partial void OnSelectedSeverityFilterChanged(string value) => await SafeLoadExceptionsAsync();
    async partial void OnSelectedStatusFilterChanged(string value) => await SafeLoadExceptionsAsync();

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _companyContext.ActiveCompanyChanged -= OnActiveCompanyChanged;
        
        lock (_ctsLock)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
        }

        _loadGate.Dispose();
    }
}
