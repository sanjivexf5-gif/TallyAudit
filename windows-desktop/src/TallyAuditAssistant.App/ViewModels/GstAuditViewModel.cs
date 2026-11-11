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
using Microsoft.Win32;
using TallyAuditAssistant.Engine.Gst;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class GstAuditViewModel : ObservableObject, INavigationAware, IDisposable
{
    private readonly IAuditRepository _repository;
    private readonly ISettingsService _settingsService;
    private readonly IActiveCompanyContext _companyContext;
    private readonly INavigationService _navigationService;
    private readonly ILogger<GstAuditViewModel> _logger;
    private readonly Gstr2BReconciliationService? _gstr2BService;

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
    private AuditException? _selectedException;

    [ObservableProperty]
    private string _auditorNoteInput = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasExceptions;

    [ObservableProperty]
    private bool _isReconciling;

    [ObservableProperty]
    private DateTime _gstr2BFromDate = GetCurrentFinancialYearStart();

    [ObservableProperty]
    private DateTime _gstr2BToDate = DateTime.Today;

    [ObservableProperty]
    private string _gstr2BSummary = string.Empty;

    public ObservableCollection<Gstr2BReconciliationItem> Gstr2BItems { get; } = new();

    public ObservableCollection<AuditException> Exceptions { get; } = new();

    public GstAuditViewModel(
        IAuditRepository repository,
        ISettingsService settingsService,
        IActiveCompanyContext companyContext,
        INavigationService navigationService,
        ILogger<GstAuditViewModel>? logger = null,
        Gstr2BReconciliationService? gstr2BService = null)
    {
        _repository = repository;
        _settingsService = settingsService;
        _companyContext = companyContext;
        _navigationService = navigationService;
        _logger = logger ?? NullLogger<GstAuditViewModel>.Instance;
        _gstr2BService = gstr2BService;

        _companyContext.ActiveCompanyChanged += OnActiveCompanyChanged;
        _ = SafeLoadGstExceptionsAsync();
    }

    public async Task OnNavigatedToAsync()
    {
        await SafeLoadGstExceptionsAsync();
    }

    private void OnActiveCompanyChanged(object? sender, Company? comp)
    {
        _ = SafeLoadGstExceptionsAsync();
    }

    private async Task SafeLoadGstExceptionsAsync()
    {
        try
        {
            await LoadGstExceptionsAsync();
        }
        catch (OperationCanceledException)
        {
            // Expected on quick succession loads / navigation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in SafeLoadGstExceptionsAsync background trigger.");
        }
    }

    [RelayCommand]
    public async Task LoadGstExceptionsAsync()
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

        _logger.LogInformation("GST findings load started. Generation: {Generation}", currentGen);

        try
        {
            await _loadGate.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("GST findings load gate wait cancelled. Generation: {Generation}", currentGen);
            return;
        }

        try
        {
            if (token.IsCancellationRequested || currentGen != Interlocked.Read(ref _loadGeneration))
            {
                _logger.LogInformation("GST findings load superseded before execution. Generation: {Generation} (Current: {CurrentGen})", currentGen, Interlocked.Read(ref _loadGeneration));
                return;
            }

            IsLoading = true;
            StatusMessage = string.Empty;

            var comp = await _companyContext.GetActiveCompanyAsync(token);
            if (token.IsCancellationRequested || currentGen != Interlocked.Read(ref _loadGeneration))
            {
                _logger.LogInformation("GST findings load superseded after company fetch. Generation: {Generation}", currentGen);
                return;
            }

            if (comp == null)
            {
                _logger.LogInformation("No active company found for GST findings. Generation: {Generation}", currentGen);
                ActiveCompanyName = "No Company Selected";
                SelectedException = null;
                Exceptions.Clear();
                HasExceptions = false;
                return;
            }

            string compId = comp.Id;
            string compName = comp.TallyCompanyName;

            _logger.LogInformation("Fetching GST exceptions from repository. Generation: {Generation}, CompanyId: {CompanyId}", currentGen, compId);

            IReadOnlyList<AuditException> list = await _repository.GetExceptionsFilteredAsync(
                compId,
                category: "GST",
                severity: "All",
                status: "All",
                searchQuery: null,
                sortBy: "Priority",
                isDescending: true,
                cancellationToken: token
            );

            if (token.IsCancellationRequested || currentGen != Interlocked.Read(ref _loadGeneration))
            {
                _logger.LogInformation("GST findings load superseded after repository query. Generation: {Generation} (Current: {CurrentGen})", currentGen, Interlocked.Read(ref _loadGeneration));
                return;
            }

            _logger.LogInformation("GST findings fetched. Generation: {Generation}, Count: {Count}", currentGen, list.Count);

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
                SelectedException = Exceptions[0];
            }
            else
            {
                SelectedException = null;
                HasExceptions = false;
            }

            _logger.LogDebug("GST findings load completed. Generation: {Generation}", currentGen);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("GST findings load cancelled during execution. Generation: {Generation}", currentGen);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading GST findings for generation {Generation}", currentGen);
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
    private async Task ImportGstr2BCsvAsync()
    {
        if (_gstr2BService == null)
        {
            StatusMessage = "GSTR-2B reconciliation is unavailable. Restart after updating the application.";
            return;
        }

        if (Gstr2BFromDate.Date > Gstr2BToDate.Date)
        {
            StatusMessage = "GSTR-2B period start date must not be after its end date.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Select GSTR-2B B2B invoice CSV export",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        IsReconciling = true;
        Gstr2BSummary = "Importing and matching GSTR-2B invoices…";
        Gstr2BItems.Clear();
        try
        {
            var company = await _companyContext.GetActiveCompanyAsync();
            if (company == null)
            {
                Gstr2BSummary = "Select an active Tally company before reconciliation.";
                return;
            }

            var report = await _gstr2BService.ReconcileCsvAsync(
                company.Id, Gstr2BFromDate, Gstr2BToDate, dialog.FileName);
            foreach (var item in report.Items) Gstr2BItems.Add(item);
            Gstr2BSummary = $"Imported {report.ImportedRows} portal invoices | Matched: {report.MatchedCount} | " +
                $"Exceptions: {report.ExceptionCount} (Value: {report.Items.Count(x => x.Status == Gstr2BMatchStatus.ValueMismatch)}, " +
                $"GSTIN: {report.Items.Count(x => x.Status == Gstr2BMatchStatus.GstinMismatch)}, " +
                $"Tally only: {report.Items.Count(x => x.Status == Gstr2BMatchStatus.BooksOnly)}, " +
                $"2B only: {report.Items.Count(x => x.Status == Gstr2BMatchStatus.Gstr2BOnly)}).";
            StatusMessage = "GSTR-2B reconciliation completed. Review every exception before deciding ITC treatment.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GSTR-2B CSV reconciliation failed.");
            Gstr2BSummary = $"Reconciliation failed: {ex.Message}";
        }
        finally
        {
            IsReconciling = false;
        }
    }

    private static DateTime GetCurrentFinancialYearStart()
    {
        var today = DateTime.Today;
        return new DateTime(today.Month >= 4 ? today.Year : today.Year - 1, 4, 1);
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
            await SafeLoadGstExceptionsAsync();
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
        else
        {
            AuditorNoteInput = string.Empty;
        }
    }

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
