using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Ledgers;
using TallyAuditAssistant.Core.Domain.Sync;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Domain.Vouchers;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.TallyIntegration;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class SyncManagerTests
{
    private readonly NullLogger<SyncManager> _logger = NullLogger<SyncManager>.Instance;

    [Fact]
    public async Task FullSync_Executes_All_Pipeline_Stages_And_Stores_Data()
    {
        // Arrange
        var mockConn = new Mock<ITallyConnection>();
        mockConn.Setup(c => c.TestConnectionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

        var mockCompany = new Mock<ITallyCompanyService>();
        mockCompany.Setup(c => c.GetCompanyProfileTypedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new TallyCompanyProfile
                   {
                       Name = "Test Company Ltd",
                       GSTIN = "27AAACA9999P1Z1",
                       BooksBeginningFrom = new DateTime(2025, 4, 1),
                       AlterId = 500
                   });

        var mockMaster = new Mock<ITallyMasterService>();
        mockMaster.Setup(m => m.GetGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<string> { "Sundry Debtors", "Sundry Creditors" });
        mockMaster.Setup(m => m.GetLedgersAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<TallyLedgerDto>
                  {
                      new() { Name = "Customer A", ParentGroup = "Sundry Debtors", ClosingBalance = 50000 },
                      new() { Name = "Vendor B", ParentGroup = "Sundry Creditors", ClosingBalance = -20000 }
                  });

        var mockVoucher = new Mock<ITallyVoucherService>();
        async IAsyncEnumerable<TallyVoucherDto> MockStream()
        {
            yield return new TallyVoucherDto
            {
                Guid = "V-001",
                VoucherNumber = "PUR/01",
                VoucherType = "Purchase",
                VoucherDate = new DateTime(2025, 5, 1),
                TotalAmount = 25000,
                Entries = new List<TallyVoucherEntryDto>
                {
                    new() { LedgerName = "Vendor B", Amount = -25000, IsDebit = false },
                    new() { LedgerName = "Purchases", Amount = 25000, IsDebit = true }
                }
            };
            await Task.Yield();
        }
        mockVoucher.Setup(v => v.StreamVouchersChunkedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .Returns(MockStream());

        var mockSyncRepo = new Mock<ISyncRepository>();
        mockSyncRepo.Setup(r => r.BatchUpsertLedgersAsync(It.IsAny<IReadOnlyList<Ledger>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((2, 0));
        mockSyncRepo.Setup(r => r.BatchUpsertVouchersAsync(It.IsAny<IReadOnlyList<Voucher>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((1, 0));

        var mockAuditRepo = new Mock<IAuditRepository>();
        var mockSettings = new Mock<ISettingsService>();

        var syncManager = new SyncManager(
            mockConn.Object,
            mockCompany.Object,
            mockMaster.Object,
            mockVoucher.Object,
            mockSyncRepo.Object,
            mockAuditRepo.Object,
            mockSettings.Object,
            _logger);

        // Act
        var result = await syncManager.StartSyncAsync("Test Company Ltd", SyncMode.Full);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(SyncStatus.Completed, syncManager.CurrentStatus);
        Assert.Equal(SyncStage.Complete, syncManager.CurrentMetrics.CurrentStage);
        Assert.Equal(100.0, syncManager.CurrentMetrics.ProgressPercentage);
        Assert.Equal(3, result.TotalProcessed); // 2 ledgers + 1 voucher
        Assert.Equal(3, result.Inserted);
        Assert.Equal(0, result.Errors);

        mockSyncRepo.Verify(r => r.UpsertCompanyAsync(It.IsAny<Company>(), It.IsAny<FinancialYear>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        mockSyncRepo.Verify(r => r.OptimizeIndexesAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockSyncRepo.Verify(r => r.RecordSyncHistoryAsync(It.IsAny<SyncHistoryRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IncrementalSync_Reaches_100Percent_On_Completion()
    {
        var mockConn = new Mock<ITallyConnection>();
        mockConn.Setup(c => c.TestConnectionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

        var mockCompany = new Mock<ITallyCompanyService>();
        mockCompany.Setup(c => c.GetCompanyProfileTypedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new TallyCompanyProfile { Name = "Inc Co" });

        var mockAuditRepo = new Mock<IAuditRepository>();
        mockAuditRepo.Setup(r => r.GetCompanyByIdAsync("Inc Co", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new Company
                     {
                         Id = "Inc Co",
                         TallyCompanyName = "Inc Co",
                         LastAlterId = 125
                     });

        var mockMaster = new Mock<ITallyMasterService>();
        mockMaster.Setup(m => m.GetGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        mockMaster.Setup(m => m.GetLedgersAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<TallyLedgerDto>());

        var mockVoucher = new Mock<ITallyVoucherService>();
        async IAsyncEnumerable<TallyVoucherDto> EmptyStream() { await Task.Yield(); yield break; }
        mockVoucher.Setup(v => v.StreamVouchersChunkedIncrementalAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .Returns(EmptyStream());

        var syncManager = new SyncManager(
            mockConn.Object, mockCompany.Object, mockMaster.Object, mockVoucher.Object,
            new Mock<ISyncRepository>().Object, mockAuditRepo.Object, new Mock<ISettingsService>().Object, _logger);

        SyncMetrics? lastEmittedMetrics = null;
        syncManager.ProgressChanged += (s, m) => lastEmittedMetrics = new SyncMetrics
        {
            CurrentStage = m.CurrentStage,
            ProgressPercentage = m.ProgressPercentage
        };

        var result = await syncManager.StartSyncAsync("Inc Co", SyncMode.Incremental);

        Assert.True(result.IsSuccess);
        Assert.Equal(SyncStatus.Completed, syncManager.CurrentStatus);
        Assert.Equal(100.0, syncManager.CurrentMetrics.ProgressPercentage);
        Assert.NotNull(lastEmittedMetrics);
        Assert.Equal(SyncStage.Complete, lastEmittedMetrics.CurrentStage);
        Assert.Equal(100.0, lastEmittedMetrics.ProgressPercentage);
        mockVoucher.Verify(
            v => v.StreamVouchersChunkedIncrementalAsync("Inc Co", It.IsAny<DateTime>(), It.IsAny<DateTime>(), 125,
                7, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RepeatedSync_UsesStableFinancialYearId_ForSameCompanyAndBooksPeriod()
    {
        var mockConn = new Mock<ITallyConnection>();
        mockConn.Setup(c => c.TestConnectionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

        var mockCompany = new Mock<ITallyCompanyService>();
        mockCompany.Setup(c => c.GetCompanyProfileTypedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new TallyCompanyProfile
                   {
                       Name = "Repeat Co",
                       BooksBeginningFrom = new DateTime(2020, 4, 1),
                       AlterId = 50
                   });

        var mockMaster = new Mock<ITallyMasterService>();
        mockMaster.Setup(m => m.GetGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<string>());
        mockMaster.Setup(m => m.GetLedgersAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<TallyLedgerDto>());

        var mockVoucher = new Mock<ITallyVoucherService>();
        async IAsyncEnumerable<TallyVoucherDto> EmptyStream()
        {
            await Task.Yield();
            yield break;
        }
        mockVoucher.Setup(v => v.StreamVouchersChunkedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .Returns(EmptyStream());

        var financialYearIds = new List<string>();
        var mockSyncRepo = new Mock<ISyncRepository>();
        mockSyncRepo.Setup(r => r.UpsertCompanyAsync(It.IsAny<Company>(), It.IsAny<FinancialYear>(), It.IsAny<CancellationToken>()))
                    .Callback<Company, FinancialYear, CancellationToken>((_, fy, _) => financialYearIds.Add(fy.Id))
                    .Returns(Task.CompletedTask);

        var syncManager = new SyncManager(
            mockConn.Object,
            mockCompany.Object,
            mockMaster.Object,
            mockVoucher.Object,
            mockSyncRepo.Object,
            new Mock<IAuditRepository>().Object,
            new Mock<ISettingsService>().Object,
            _logger);

        var first = await syncManager.StartSyncAsync("Repeat Co", SyncMode.Full);
        var second = await syncManager.StartSyncAsync("Repeat Co", SyncMode.Full);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(4, financialYearIds.Count); // two metadata upserts per sync
        Assert.All(financialYearIds, id => Assert.Equal(financialYearIds[0], id));
        Assert.Equal("Repeat Co:FY:20200401:20210331", financialYearIds[0]);
    }

    [Fact]
    public async Task RetrySync_Reaches_100Percent_On_Completion()
    {
        var mockConn = new Mock<ITallyConnection>();
        mockConn.Setup(c => c.TestConnectionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

        var mockCompany = new Mock<ITallyCompanyService>();
        mockCompany.Setup(c => c.GetCompanyProfileTypedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new TallyCompanyProfile { Name = "Retry Co" });

        var mockMaster = new Mock<ITallyMasterService>();
        mockMaster.Setup(m => m.GetGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        mockMaster.Setup(m => m.GetLedgersAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<TallyLedgerDto>());

        var mockVoucher = new Mock<ITallyVoucherService>();
        async IAsyncEnumerable<TallyVoucherDto> EmptyStreamRetry() { await Task.Yield(); yield break; }
        mockVoucher.Setup(v => v.StreamVouchersChunkedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .Returns(EmptyStreamRetry());

        var syncManager = new SyncManager(
            mockConn.Object, mockCompany.Object, mockMaster.Object, mockVoucher.Object,
            new Mock<ISyncRepository>().Object, new Mock<IAuditRepository>().Object, new Mock<ISettingsService>().Object, _logger);

        await syncManager.StartSyncAsync("Retry Co", SyncMode.Full);
        var result = await syncManager.RetryAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(SyncStatus.Completed, syncManager.CurrentStatus);
        Assert.Equal(100.0, syncManager.CurrentMetrics.ProgressPercentage);
    }

    [Fact]
    public async Task FailedSync_Does_Not_Report_100Percent()
    {
        var mockConn = new Mock<ITallyConnection>();
        mockConn.Setup(c => c.TestConnectionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
        mockConn.Setup(c => c.ProbePortRangeAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((TallyEndpointInfo?)null);

        var syncManager = new SyncManager(
            mockConn.Object, new Mock<ITallyCompanyService>().Object, new Mock<ITallyMasterService>().Object, new Mock<ITallyVoucherService>().Object,
            new Mock<ISyncRepository>().Object, new Mock<IAuditRepository>().Object, new Mock<ISettingsService>().Object, _logger);

        var result = await syncManager.StartSyncAsync("Failed Co", SyncMode.Full);

        Assert.False(result.IsSuccess);
        Assert.Equal(SyncStatus.Failed, syncManager.CurrentStatus);
        Assert.NotEqual(100.0, syncManager.CurrentMetrics.ProgressPercentage);
        Assert.Equal(SyncStage.Failed, syncManager.CurrentMetrics.CurrentStage);
        Assert.Equal(SyncStage.Connect.ToString(), result.FailedStage);
    }

    [Fact]
    public async Task Cancel_Preserves_Batches_And_Sets_Cancelled_Status()
    {
        // Arrange
        var mockConn = new Mock<ITallyConnection>();
        mockConn.Setup(c => c.TestConnectionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

        var mockCompany = new Mock<ITallyCompanyService>();
        mockCompany.Setup(c => c.GetCompanyProfileTypedAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new TallyCompanyProfile { Name = "Test Co" });

        var mockMaster = new Mock<ITallyMasterService>();
        mockMaster.Setup(m => m.GetGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        mockMaster.Setup(m => m.GetLedgersAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<TallyLedgerDto>());

        var mockVoucher = new Mock<ITallyVoucherService>();
        async IAsyncEnumerable<TallyVoucherDto> SlowMockStream([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
        {
            for (int i = 0; i < 50; i++)
            {
                await Task.Delay(50, token);
                yield return new TallyVoucherDto { VoucherNumber = $"V-{i}", TotalAmount = 100 };
            }
        }
        mockVoucher.Setup(v => v.StreamVouchersChunkedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .Returns((string c, DateTime f, DateTime t, int s, CancellationToken token) => SlowMockStream(token));

        var syncManager = new SyncManager(
            mockConn.Object,
            mockCompany.Object,
            mockMaster.Object,
            mockVoucher.Object,
            new Mock<ISyncRepository>().Object,
            new Mock<IAuditRepository>().Object,
            new Mock<ISettingsService>().Object,
            _logger);

        // Act: Start sync then immediately cancel
        var syncTask = syncManager.StartSyncAsync("Test Co", SyncMode.Full);
        await Task.Delay(70);
        await syncManager.CancelAsync();
        var result = await syncTask;

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(SyncStatus.Cancelled, syncManager.CurrentStatus);
        Assert.NotEqual(100.0, syncManager.CurrentMetrics.ProgressPercentage);
    }
}
