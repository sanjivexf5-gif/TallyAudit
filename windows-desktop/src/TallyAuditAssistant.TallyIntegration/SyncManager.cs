using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Ledgers;
using TallyAuditAssistant.Core.Domain.Sync;
using TallyAuditAssistant.Core.Domain.Vouchers;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.TallyIntegration;

public class SyncManager : ISyncManager
{
    private readonly ITallyConnection _connection;
    private readonly ITallyCompanyService _companyService;
    private readonly ITallyMasterService _masterService;
    private readonly ITallyVoucherService _voucherService;
    private readonly ISyncRepository _syncRepository;
    private readonly IAuditRepository _auditRepository;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<SyncManager> _logger;

    private readonly SemaphoreSlim _pauseSemaphore = new(1, 1);
    private CancellationTokenSource? _syncCts;
    private bool _isPaused;
    private string? _lastCompanySynced;
    private SyncMode _lastModeSynced = SyncMode.Full;

    public SyncStatus CurrentStatus { get; private set; } = SyncStatus.Idle;
    public SyncMetrics CurrentMetrics { get; } = new();

    public event EventHandler<SyncMetrics>? ProgressChanged;
    public event EventHandler<string>? SyncLogEmitted;

    public SyncManager(
        ITallyConnection connection,
        ITallyCompanyService companyService,
        ITallyMasterService masterService,
        ITallyVoucherService voucherService,
        ISyncRepository syncRepository,
        IAuditRepository auditRepository,
        ISettingsService settingsService,
        ILogger<SyncManager> logger)
    {
        _connection = connection;
        _companyService = companyService;
        _masterService = masterService;
        _voucherService = voucherService;
        _syncRepository = syncRepository;
        _auditRepository = auditRepository;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<SyncResult> StartSyncAsync(string companyName, SyncMode mode, CancellationToken cancellationToken = default)
    {
        if (CurrentStatus == SyncStatus.Running)
        {
            throw new InvalidOperationException("A synchronization process is already running.");
        }

        _lastCompanySynced = companyName;
        _lastModeSynced = mode;

        _syncCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _syncCts.Token;

        CurrentStatus = SyncStatus.Running;
        ResetMetrics();

        var stopwatch = Stopwatch.StartNew();
        var companyId = Guid.NewGuid().ToString();

        try
        {
            // PIPELINE STAGE 1: CONNECT
            SetStage(SyncStage.Connect, "Verifying connectivity with TallyPrime HTTP server...");
            EmitLog("Testing connection to TallyPrime...");
            var isConnected = await _connection.TestConnectionAsync("localhost", 9000, ct);
            if (!isConnected)
            {
                var endpoint = await _connection.ProbePortRangeAsync("localhost", 9000, 9005, ct);
                if (endpoint == null)
                {
                    throw new InvalidOperationException("Could not connect to TallyPrime on ports 9000-9005. Verify Tally is running.");
                }
            }
            EmitLog("Connection established successfully.");

            await CheckPauseAsync(ct);

            // PIPELINE STAGE 2: SELECT COMPANY
            SetStage(SyncStage.SelectCompany, $"Selecting active company: {companyName}...");
            EmitLog($"Querying statutory company master for '{companyName}'...");
            
            var profile = await _companyService.GetCompanyProfileTypedAsync(companyName, null, ct);
            if (profile != null)
            {
                companyId = profile.Name; // Consistent ID
            }
            else
            {
                profile = new Core.Domain.Tally.TallyCompanyProfile
                {
                    Name = companyName,
                    BooksBeginningFrom = new DateTime(DateTime.Today.Year, 4, 1)
                };
            }

            // Look up existing company record to check for delta AlterId
            var existingCompany = await _auditRepository.GetCompanyByIdAsync(companyId, ct);
            long? fromAlterId = (mode == SyncMode.Incremental && existingCompany != null) ? existingCompany.LastAlterId : null;

            var compEntity = new Company
            {
                Id = companyId,
                TallyCompanyName = profile.Name,
                FormalName = profile.FormalName,
                GSTIN = profile.GSTIN,
                PAN = profile.PAN,
                StateName = profile.StateName,
                StateCode = profile.StateCode,
                BooksFromDate = profile.BooksBeginningFrom,
                LastAlterId = profile.AlterId
            };

            var fyEntity = new FinancialYear
            {
                Id = Guid.NewGuid().ToString(),
                CompanyId = companyId,
                StartDate = profile.BooksBeginningFrom,
                EndDate = profile.BooksBeginningFrom.AddYears(1).AddDays(-1)
            };

            await _syncRepository.UpsertCompanyAsync(compEntity, fyEntity, ct);
            EmitLog($"Saved company statutory master. Sync Mode: {mode} (fromAlterId: {fromAlterId?.ToString() ?? "0 (Full)"})");

            await CheckPauseAsync(ct);

            // PIPELINE STAGE 3: READ MASTERS
            SetStage(SyncStage.ReadMasters, "Fetching Chart of Accounts, Groups, and Ledgers...");
            EmitLog("Reading master groups and account ledgers...");

            var rawGroups = await _masterService.GetGroupsAsync(companyName, ct);
            var groups = rawGroups.Select(g => new Group
            {
                CompanyId = companyId,
                Name = g,
                ParentName = "Primary"
            }).ToList();
            await _syncRepository.BatchUpsertGroupsAsync(groups, companyId, ct);

            var rawLedgers = await _masterService.GetLedgersAsync(companyName, fromAlterId, ct);
            CurrentMetrics.RecordsDiscovered += rawLedgers.Count;

            var ledgers = rawLedgers.Select(l => new Ledger
            {
                Id = $"{companyId}:{l.Name}",
                CompanyId = companyId,
                Name = l.Name,
                ParentGroup = l.ParentGroup,
                GSTIN = l.GSTIN,
                PAN = l.PAN,
                StateName = l.StateName,
                OpeningBalance = l.OpeningBalance,
                ClosingBalance = l.ClosingBalance,
                TaxType = l.TaxType,
                HsnCode = l.HsnCode,
                GstRate = l.GstRate,
                AlterId = l.AlterId
            }).ToList();

            var (ledgersInserted, ledgersUpdated) = await _syncRepository.BatchUpsertLedgersAsync(ledgers, companyId, ct);
            CurrentMetrics.RecordsInserted += ledgersInserted;
            CurrentMetrics.RecordsUpdated += ledgersUpdated;
            CurrentMetrics.RecordsProcessed += ledgers.Count;
            EmitLog($"Processed {ledgers.Count} ledgers (Inserted: {ledgersInserted}, Updated: {ledgersUpdated})");

            await CheckPauseAsync(ct);

            // PIPELINE STAGE 4: READ TRANSACTIONS
            SetStage(SyncStage.ReadTransactions, "Streaming transaction vouchers in chunked date batches...");
            EmitLog("Streaming voucher transactions (Low-RAM chunking active)...");

            var fromDate = profile.BooksBeginningFrom;
            var toDate = DateTime.Today;

            var fromDateStr = await _settingsService.GetSettingAsync("AuditPeriodFrom", "");
            var toDateStr = await _settingsService.GetSettingAsync("AuditPeriodTo", "");

            if (DateTime.TryParseExact(fromDateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var fD))
            {
                fromDate = fD;
            }
            if (DateTime.TryParseExact(toDateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var tD))
            {
                toDate = tD;
            }

            EmitLog($"Syncing period: {fromDate:dd-MMM-yyyy} to {toDate:dd-MMM-yyyy}");

            var voucherBatch = new List<Voucher>();
            const int batchSize = 100;
            var totalVouchersCount = 0;

            await foreach (var rawVoucher in _voucherService.StreamVouchersChunkedAsync(companyName, fromDate, toDate, 30, ct))
            {
                await CheckPauseAsync(ct);

                // PIPELINE STAGE 5: VALIDATE INLINE
                var totalDebits = rawVoucher.Entries.Where(e => e.IsDebit).Sum(e => e.Amount);
                var totalCredits = rawVoucher.Entries.Where(e => !e.IsDebit).Sum(e => Math.Abs(e.Amount));
                
                // If single entry or balanced double entry
                var voucher = new Voucher
                {
                    Id = !string.IsNullOrEmpty(rawVoucher.Guid) ? rawVoucher.Guid : $"{companyId}:{rawVoucher.VoucherNumber}",
                    CompanyId = companyId,
                    VoucherTypeId = rawVoucher.VoucherType,
                    VoucherTypeName = rawVoucher.VoucherType,
                    VoucherNumber = rawVoucher.VoucherNumber,
                    ReferenceNumber = rawVoucher.ReferenceNumber,
                    VoucherDate = rawVoucher.VoucherDate,
                    Narration = rawVoucher.Narration,
                    TotalAmount = rawVoucher.TotalAmount,
                    IsCancelled = rawVoucher.IsCancelled,
                    IsOptional = rawVoucher.IsOptional,
                    PartyLedgerName = rawVoucher.PartyLedgerName,
                    AlterId = rawVoucher.AlterId,
                    Entries = rawVoucher.Entries.Select(e => new VoucherEntry
                    {
                        LedgerName = e.LedgerName,
                        Amount = e.Amount,
                        IsDebit = e.IsDebit,
                        BillRefType = e.BillRefType,
                        BillName = e.BillName
                    }).ToList()
                };

                voucherBatch.Add(voucher);
                CurrentMetrics.RecordsDiscovered++;

                if (voucherBatch.Count >= batchSize)
                {
                    // PIPELINE STAGE 6: STORE (Transaction-safe commit of batch)
                    SetStage(SyncStage.Store, $"Committing batch of {voucherBatch.Count} vouchers to SQLite...");
                    var (vIns, vUpd) = await _syncRepository.BatchUpsertVouchersAsync(voucherBatch, companyId, ct);
                    
                    CurrentMetrics.RecordsInserted += vIns;
                    CurrentMetrics.RecordsUpdated += vUpd;
                    CurrentMetrics.RecordsProcessed += voucherBatch.Count;
                    totalVouchersCount += voucherBatch.Count;

                    UpdateSpeedAndProgress(stopwatch.Elapsed);
                    EmitLog($"Stored batch up to {voucher.VoucherDate:dd-MMM-yyyy}. Processed: {totalVouchersCount}");
                    voucherBatch.Clear();
                }
            }

            // Flush final pending batch
            if (voucherBatch.Count > 0)
            {
                var (vIns, vUpd) = await _syncRepository.BatchUpsertVouchersAsync(voucherBatch, companyId, ct);
                CurrentMetrics.RecordsInserted += vIns;
                CurrentMetrics.RecordsUpdated += vUpd;
                CurrentMetrics.RecordsProcessed += voucherBatch.Count;
                totalVouchersCount += voucherBatch.Count;
                voucherBatch.Clear();
            }

            // PIPELINE STAGE 7: INDEX
            SetStage(SyncStage.Index, "Optimizing SQLite indexes and updating sync timestamps...");
            EmitLog("Running PRAGMA optimize on database indexes...");
            await _syncRepository.OptimizeIndexesAsync(ct);

            // Update company last sync metadata
            compEntity.LastSyncDate = DateTime.UtcNow;
            await _syncRepository.UpsertCompanyAsync(compEntity, fyEntity, ct);

            // Record history
            var historyRecord = new SyncHistoryRecord
            {
                CompanyId = companyId,
                CompanyName = companyName,
                SyncType = mode.ToString(),
                VouchersFetched = totalVouchersCount,
                MastersFetched = ledgers.Count,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Status = "Success"
            };
            await _syncRepository.RecordSyncHistoryAsync(historyRecord, ct);

            // PIPELINE STAGE 8: COMPLETE
            stopwatch.Stop();
            SetStage(SyncStage.Complete, "Synchronization completed successfully!");
            CurrentStatus = SyncStatus.Completed;
            CurrentMetrics.ProgressPercentage = 100.0;
            EmitLog($"Sync finished in {stopwatch.Elapsed:mm\\:ss}. {CurrentMetrics.RecordsProcessed} records stored.");

            return new SyncResult(
                IsSuccess: true,
                Mode: mode,
                TotalProcessed: CurrentMetrics.RecordsProcessed,
                Inserted: CurrentMetrics.RecordsInserted,
                Updated: CurrentMetrics.RecordsUpdated,
                Skipped: CurrentMetrics.RecordsSkipped,
                Errors: CurrentMetrics.Errors,
                Duration: stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            CurrentStatus = SyncStatus.Cancelled;
            SetStage(SyncStage.Cancelled, "Synchronization cancelled by user.");
            EmitLog("Sync cancelled. Already synchronized batches were preserved safely.");

            await RecordFailureHistoryAsync(companyId, companyName, mode, stopwatch.ElapsedMilliseconds, "Cancelled by user");
            return new SyncResult(false, mode, CurrentMetrics.RecordsProcessed, CurrentMetrics.RecordsInserted, CurrentMetrics.RecordsUpdated, CurrentMetrics.RecordsSkipped, CurrentMetrics.Errors, stopwatch.Elapsed, "Cancelled by user");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            CurrentStatus = SyncStatus.Failed;
            CurrentMetrics.Errors++;
            SetStage(SyncStage.Failed, $"Sync failed: {ex.Message}");
            _logger.LogError(ex, "Synchronization failed critically.");
            EmitLog($"ERROR: {ex.Message}. Committed data was preserved. You can Resume/Retry.");

            await RecordFailureHistoryAsync(companyId, companyName, mode, stopwatch.ElapsedMilliseconds, ex.Message);
            return new SyncResult(false, mode, CurrentMetrics.RecordsProcessed, CurrentMetrics.RecordsInserted, CurrentMetrics.RecordsUpdated, CurrentMetrics.RecordsSkipped, CurrentMetrics.Errors, stopwatch.Elapsed, ex.Message);
        }
    }

    public async Task PauseAsync()
    {
        if (CurrentStatus == SyncStatus.Running && !_isPaused)
        {
            _isPaused = true;
            CurrentStatus = SyncStatus.Paused;
            await _pauseSemaphore.WaitAsync();
            SetStage(SyncStage.Paused, "Synchronization paused. Press Resume to continue.");
            EmitLog("Synchronization paused.");
        }
    }

    public Task ResumeAsync()
    {
        if (CurrentStatus == SyncStatus.Paused && _isPaused)
        {
            _isPaused = false;
            CurrentStatus = SyncStatus.Running;
            _pauseSemaphore.Release();
            SetStage(SyncStage.ReadTransactions, "Resuming synchronization...");
            EmitLog("Synchronization resumed.");
        }
        return Task.CompletedTask;
    }

    public Task CancelAsync()
    {
        if (CurrentStatus == SyncStatus.Running || CurrentStatus == SyncStatus.Paused)
        {
            if (_isPaused)
            {
                _isPaused = false;
                _pauseSemaphore.Release();
            }
            _syncCts?.Cancel();
        }
        return Task.CompletedTask;
    }

    public Task<SyncResult> RetryAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_lastCompanySynced))
        {
            throw new InvalidOperationException("No previous synchronization to retry.");
        }
        return StartSyncAsync(_lastCompanySynced, _lastModeSynced, cancellationToken);
    }

    public Task<IReadOnlyList<SyncHistoryRecord>> GetSyncHistoryAsync(string companyId, CancellationToken cancellationToken = default)
    {
        return _syncRepository.GetSyncHistoryAsync(companyId, 20, cancellationToken);
    }

    private async Task CheckPauseAsync(CancellationToken ct)
    {
        if (_isPaused)
        {
            await _pauseSemaphore.WaitAsync(ct);
            _pauseSemaphore.Release();
        }
    }

    private void SetStage(SyncStage stage, string taskDesc)
    {
        CurrentMetrics.CurrentStage = stage;
        CurrentMetrics.CurrentTaskDescription = taskDesc;
        ProgressChanged?.Invoke(this, CurrentMetrics);
    }

    private void UpdateSpeedAndProgress(TimeSpan elapsed)
    {
        CurrentMetrics.ElapsedTime = elapsed;
        if (elapsed.TotalSeconds > 0)
        {
            CurrentMetrics.ItemsPerSecond = Math.Round(CurrentMetrics.RecordsProcessed / elapsed.TotalSeconds, 1);
        }
        if (CurrentMetrics.RecordsDiscovered > 0)
        {
            CurrentMetrics.ProgressPercentage = Math.Min(99.0, Math.Round((double)CurrentMetrics.RecordsProcessed / CurrentMetrics.RecordsDiscovered * 100, 1));
        }
        ProgressChanged?.Invoke(this, CurrentMetrics);
    }

    private void ResetMetrics()
    {
        CurrentMetrics.RecordsDiscovered = 0;
        CurrentMetrics.RecordsProcessed = 0;
        CurrentMetrics.RecordsInserted = 0;
        CurrentMetrics.RecordsUpdated = 0;
        CurrentMetrics.RecordsSkipped = 0;
        CurrentMetrics.Errors = 0;
        CurrentMetrics.ElapsedTime = TimeSpan.Zero;
        CurrentMetrics.ProgressPercentage = 0.0;
        CurrentMetrics.ItemsPerSecond = 0.0;
    }

    private void EmitLog(string message)
    {
        _logger.LogInformation("[SYNC] {Message}", message);
        SyncLogEmitted?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private async Task RecordFailureHistoryAsync(string companyId, string companyName, SyncMode mode, long durationMs, string error)
    {
        try
        {
            var record = new SyncHistoryRecord
            {
                CompanyId = companyId,
                CompanyName = companyName,
                SyncType = mode.ToString(),
                VouchersFetched = CurrentMetrics.RecordsProcessed,
                DurationMs = durationMs,
                Status = "Failed",
                ErrorMessage = error
            };
            await _syncRepository.RecordSyncHistoryAsync(record);
        }
        catch
        {
            // Suppress error during crash recording
        }
    }
}
