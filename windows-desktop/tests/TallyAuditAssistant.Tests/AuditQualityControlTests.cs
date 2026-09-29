using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.Engine.Services;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class AuditQualityControlTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly AuditRepository _auditRepository;
    private readonly AuditFinalizationRepository _finalizationRepository;
    private readonly Mock<IAuditTrailService> _mockAuditTrail;
    private readonly AuditFinalizationService _finalizationService;
    private readonly AuditQualityControlService _qcService;

    public AuditQualityControlTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"qc_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance, _testDbPath);
        _auditRepository = new AuditRepository(_factory);
        _finalizationRepository = new AuditFinalizationRepository(_factory);
        _mockAuditTrail = new Mock<IAuditTrailService>();
        _finalizationService = new AuditFinalizationService(_finalizationRepository, _mockAuditTrail.Object);
        _qcService = new AuditQualityControlService(_finalizationRepository, _finalizationService, _auditRepository, _mockAuditTrail.Object);
    }

    public async Task InitializeAsync()
    {
        await _initializer.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
        }
        catch { }
    }

    private async Task SaveCompanyAsync(string companyId)
    {
        await _auditRepository.SaveCompanyAsync(new Company
        {
            Id = companyId,
            TallyCompanyName = $"Company {companyId}",
            BooksFromDate = new DateTime(2025, 4, 1)
        });
    }

    [Fact]
    public async Task GetQualityControlSummary_WithEmptyState_ReturnsReadyForReviewFalse()
    {
        string companyId = "COMP-QC-01";
        string periodId = "FY-2025-26";
        await SaveCompanyAsync(companyId);

        var summary = await _qcService.GetQualityControlSummaryAsync(companyId, periodId);

        Assert.NotNull(summary);
        Assert.False(summary.IsReadyForReview);
        Assert.False(summary.IsReadyForFinalization);
        Assert.True(summary.WarningsCount > 0);
    }

    [Fact]
    public async Task GetQualityControlSummary_WithCompleteEngagement_ReturnsReadyForFinalizationTrue()
    {
        string companyId = "COMP-QC-02";
        string periodId = "FY-2025-26";
        await SaveCompanyAsync(companyId);

        var state = await _finalizationService.GetOrCreateStateAsync(companyId, periodId);
        
        // Mark all checklist items complete
        var checklist = await _finalizationRepository.GetChecklistAsync(state.Id);
        foreach (var item in checklist)
        {
            await _finalizationService.SetChecklistItemCompletedAsync(item.Id, true, "Auditor-S", "Validated successfully.");
        }

        // Set auditor conclusion
        state.AuditorConclusionText = "Unqualified audit opinion.";
        await _finalizationRepository.SaveStateAsync(state);

        var summary = await _qcService.GetQualityControlSummaryAsync(companyId, periodId);

        Assert.NotNull(summary);
        Assert.True(summary.IsReadyForReview);
        // Note: Ready for finalization requires 0 open items & 0 open notes & 0 unreviewed exceptions
        // In this test, all checklist items are completed and there are no exceptions or open items generated, so it should be true.
        Assert.True(summary.IsReadyForFinalization);
    }

    [Fact]
    public async Task GetQualityControlSummary_CancellationSupported()
    {
        string companyId = "COMP-QC-03";
        string periodId = "FY-2025-26";
        await SaveCompanyAsync(companyId);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _qcService.GetQualityControlSummaryAsync(companyId, periodId, cts.Token));
    }

    [Fact]
    public async Task CompanyIsolation_IsStrictlyMaintained()
    {
        string compA = "COMP-QC-A";
        string compB = "COMP-QC-B";
        string periodId = "FY-2025-26";
        await SaveCompanyAsync(compA);
        await SaveCompanyAsync(compB);

        var stateA = await _finalizationService.GetOrCreateStateAsync(compA, periodId);
        var checklistA = await _finalizationRepository.GetChecklistAsync(stateA.Id);
        await _finalizationService.SetChecklistItemCompletedAsync(checklistA[0].Id, true, "Auditor-S", "Complete");

        var summaryA = await _qcService.GetQualityControlSummaryAsync(compA, periodId);
        var summaryB = await _qcService.GetQualityControlSummaryAsync(compB, periodId);

        Assert.NotEqual(summaryA.PassedChecksCount, summaryB.PassedChecksCount);
    }
}
