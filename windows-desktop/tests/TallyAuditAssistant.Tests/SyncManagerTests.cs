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
        Assert.Equal(3, result.TotalProcessed); // 2 ledgers + 1 voucher
        Assert.Equal(3, result.Inserted);
        Assert.Equal(0, result.Errors);

        mockSyncRepo.Verify(r => r.UpsertCompanyAsync(It.IsAny<Company>(), It.IsAny<FinancialYear>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        mockSyncRepo.Verify(r => r.OptimizeIndexesAsync(It.IsAny<CancellationToken>()), Times.Once);
        mockSyncRepo.Verify(r => r.RecordSyncHistoryAsync(It.IsAny<SyncHistoryRecord>(), It.IsAny<CancellationToken>()), Times.Once);
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
    }
}
