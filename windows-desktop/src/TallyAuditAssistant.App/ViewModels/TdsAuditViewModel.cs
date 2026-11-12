using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using Microsoft.Win32;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Engine.Tds.Reconciliation;

namespace TallyAuditAssistant.App.ViewModels;

public partial class TdsAuditViewModel : ObservableObject, INavigationAware, IDisposable
{
    private readonly IAuditRepository _repository;
    private readonly ISettingsService _settingsService;
    private readonly IActiveCompanyContext _companyContext;
    private readonly INavigationService _navigationService;
    private readonly ILogger<TdsAuditViewModel> _logger;
    private readonly TdsChallanReturnReconciliationService _reconciliationService;
    private readonly ITdsRepository? _tdsRepository;
    private IReadOnlyList<TdsChallanRecord> _importedChallans = Array.Empty<TdsChallanRecord>();
    private IReadOnlyList<TdsReturnLine> _importedReturnLines = Array.Empty<TdsReturnLine>();

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

    public ObservableCollection<AuditException> Exceptions { get; } = new();
    public ObservableCollection<TdsReconciliationFinding> ReconciliationFindings { get; } = new();

    [ObservableProperty] private string _reconciliationStatus = "Import a challan CSV and a TDS return allocation CSV to begin.";
    [ObservableProperty] private int _challanRecordCount;
    [ObservableProperty] private int _returnLineCount;
    [ObservableProperty] private string _ledgerComparisonStatus = "Import return allocation data, then compare it with Tally TDS ledger movements.";

    public TdsAuditViewModel(
        IAuditRepository repository,
        ISettingsService settingsService,
        IActiveCompanyContext companyContext,
        INavigationService navigationService,
        ILogger<TdsAuditViewModel>? logger = null,
        TdsChallanReturnReconciliationService? reconciliationService = null,
        ITdsRepository? tdsRepository = null)
    {
        _repository = repository;
        _settingsService = settingsService;
        _companyContext = companyContext;
        _navigationService = navigationService;
        _logger = logger ?? NullLogger<TdsAuditViewModel>.Instance;
        _reconciliationService = reconciliationService ?? new TdsChallanReturnReconciliationService();
        _tdsRepository = tdsRepository;

        _companyContext.ActiveCompanyChanged += OnActiveCompanyChanged;
        _ = SafeLoadTdsExceptionsAsync();
    }

    public async Task OnNavigatedToAsync()
    {
        await SafeLoadTdsExceptionsAsync();
    }

    private void OnActiveCompanyChanged(object? sender, Company? comp)
    {
        _ = SafeLoadTdsExceptionsAsync();
    }

    private async Task SafeLoadTdsExceptionsAsync()
    {
        try
        {
            await LoadTdsExceptionsAsync();
        }
        catch (OperationCanceledException)
        {
            // Expected on fast succession loads
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in SafeLoadTdsExceptionsAsync background trigger.");
        }
    }

    [RelayCommand]
    public async Task LoadTdsExceptionsAsync()
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

        _logger.LogInformation("TDS findings load started. Generation: {Generation}", currentGen);

        try
        {
            await _loadGate.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("TDS findings load gate wait cancelled. Generation: {Generation}", currentGen);
            return;
        }

        try
        {
            if (token.IsCancellationRequested || currentGen != Interlocked.Read(ref _loadGeneration))
            {
                _logger.LogInformation("TDS findings load superseded before execution. Generation: {Generation} (Current: {CurrentGen})", currentGen, Interlocked.Read(ref _loadGeneration));
                return;
            }

            IsLoading = true;
            StatusMessage = string.Empty;

            var comp = await _companyContext.GetActiveCompanyAsync(token);
            if (token.IsCancellationRequested || currentGen != Interlocked.Read(ref _loadGeneration))
            {
                _logger.LogInformation("TDS findings load superseded after company fetch. Generation: {Generation}", currentGen);
                return;
            }

            if (comp == null)
            {
                _logger.LogInformation("No active company found for TDS findings. Generation: {Generation}", currentGen);
                ActiveCompanyName = "No Company Selected";
                SelectedException = null;
                Exceptions.Clear();
                HasExceptions = false;
                return;
            }

            string compId = comp.Id;
            string compName = comp.TallyCompanyName;

            _logger.LogInformation("Fetching TDS exceptions from repository. Generation: {Generation}, CompanyId: {CompanyId}", currentGen, compId);

            IReadOnlyList<AuditException> list = await _repository.GetExceptionsFilteredAsync(
                compId,
                category: "TDS",
                severity: "All",
                status: "All",
                searchQuery: null,
                sortBy: "Priority",
                isDescending: true,
                cancellationToken: token
            );

            if (token.IsCancellationRequested || currentGen != Interlocked.Read(ref _loadGeneration))
            {
                _logger.LogInformation("TDS findings load superseded after repository query. Generation: {Generation} (Current: {CurrentGen})", currentGen, Interlocked.Read(ref _loadGeneration));
                return;
            }

            _logger.LogInformation("TDS findings fetched. Generation: {Generation}, Count: {Count}", currentGen, list.Count);

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

            _logger.LogDebug("TDS findings load completed. Generation: {Generation}", currentGen);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("TDS findings load cancelled during execution. Generation: {Generation}", currentGen);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading TDS findings for generation {Generation}", currentGen);
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
    private void ImportChallans()
    {
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*", Title = "Select normalized TDS challan CSV" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _importedChallans = _reconciliationService.ReadChallansCsv(ReadCsvFile(dialog.FileName));
            ChallanRecordCount = _importedChallans.Count;
            ReconcileImportedTdsData();
        }
        catch (Exception ex)
        {
            ReconciliationStatus = $"Challan import failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ImportReturnStatement()
    {
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*", Title = "Select normalized TDS return allocation CSV" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _importedReturnLines = _reconciliationService.ReadReturnCsv(ReadCsvFile(dialog.FileName));
            ReturnLineCount = _importedReturnLines.Count;
            ReconcileImportedTdsData();
        }
        catch (Exception ex)
        {
            ReconciliationStatus = $"Return import failed: {ex.Message}";
        }
    }

    private static string ReadCsvFile(string path)
    {
        var file = new FileInfo(path);
        const long maximumBytes = 50 * 1024 * 1024;
        if (file.Length > maximumBytes)
            throw new InvalidOperationException("CSV file exceeds the 50 MB safety limit. Split the export into smaller period-wise files.");
        return File.ReadAllText(path);
    }

    private void ReconcileImportedTdsData()
    {
        if (_importedChallans.Count == 0 || _importedReturnLines.Count == 0)
        {
            ReconciliationStatus = $"{ChallanRecordCount} challan row(s) and {ReturnLineCount} return allocation row(s) imported. Import both files to reconcile.";
            return;
        }

        var findings = _reconciliationService.Reconcile(_importedChallans, _importedReturnLines);
        ReconciliationFindings.Clear();
        foreach (var finding in findings) ReconciliationFindings.Add(finding);
        var matched = findings.Count(x => x.Status == "Matched");
        ReconciliationStatus = $"{findings.Count} CIN group(s) checked • {matched} matched • {findings.Count - matched} requiring review.";
    }


    [RelayCommand]
    private async Task CompareWithTallyLedgersAsync()
    {
        if (_tdsRepository == null) { LedgerComparisonStatus = "Tally ledger comparison is unavailable because the TDS repository is not configured."; return; }
        if (_importedReturnLines.Count == 0) { LedgerComparisonStatus = "Import a TDS return allocation CSV first."; return; }
        try
        {
            var company = await _companyContext.GetActiveCompanyAsync();
            if (company == null) { LedgerComparisonStatus = "Select a company before comparing ledger movements."; return; }
            var years = _importedReturnLines.Select(x => x.FinancialYear).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var quarters = _importedReturnLines.Select(x => x.Quarter).Distinct().ToArray();
            if (years.Length != 1 || quarters.Length != 1) { LedgerComparisonStatus = "Import one financial year and one quarter at a time."; return; }
            var parts = years[0].Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var startYear) || !int.TryParse(parts[1], out var endYear))
            { LedgerComparisonStatus = "Financial year must use YYYY-YY format, for example 2026-27."; return; }
            if (endYear < 100) endYear += (startYear / 100) * 100;
            var quarter = quarters[0];
            var from = new DateTime(startYear, 4, 1).AddMonths((quarter - 1) * 3);
            var to = quarter == 4 ? new DateTime(endYear, 3, 31) : from.AddMonths(3).AddDays(-1);
            var movements = await _tdsRepository.GetTdsLedgerMovementsAsync(company.Id, from, to);
            var statement = _importedReturnLines.Sum(x => x.DeductedAmount);
            var credits = movements.Sum(x => x.CreditTotal);
            var debits = movements.Sum(x => x.DebitTotal);
            var details = movements.Count == 0 ? "No TDS ledgers identified." :
                string.Join("; ", movements.Select(x => $"{x.LedgerName}: Cr {x.CreditTotal:N2} / Dr {x.DebitTotal:N2}"));
            LedgerComparisonStatus = $"FY {years[0]} Q{quarter} | Return allocations {statement:N2} | Tally TDS credits {credits:N2} | Difference (credits − return) {credits - statement:N2} | Tally debits {debits:N2}. {details} Check ledger classification, timing and journal adjustments. Movement comparison only; it does not prove challan payment or return filing.";
        }
        catch (Exception ex) { LedgerComparisonStatus = $"Ledger comparison failed: {ex.Message}"; }
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
            await SafeLoadTdsExceptionsAsync();
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
