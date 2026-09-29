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

    private async Task SaveVoucherAsync(string companyId)
    {
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO Vouchers (Id, CompanyId, VoucherTypeId, VoucherTypeName, VoucherNumber, VoucherDate, TotalAmount, AlterId)
            VALUES (@Id, @CompanyId, 'Sales', 'Sales', '1', '2025-04-01', 100, 1);
        ", new { Id = Guid.NewGuid().ToString(), CompanyId = companyId });
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
        await SaveVoucherAsync(companyId);

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

    [Fact]
    public async Task GetQualityControlSummary_WithPendingFinding_ReturnsAttentionRequired()
    {
        string companyId = "COMP-QC-04";
        string periodId = "FY-2025-26";
        await SaveCompanyAsync(companyId);
        await SaveVoucherAsync(companyId);

        var state = await _finalizationService.GetOrCreateStateAsync(companyId, periodId);

        // Add a pending finding using Dapper
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO AuditResults (ResultId, CompanyId, RuleId, RuleName, Category, Severity, Evidence, ReviewStatus)
            VALUES (@ResultId, @CompanyId, 'R1', 'Rule 1', 1, 1, '{}', 0);
        ", new { ResultId = "EXC-QC-01", CompanyId = companyId });

        var summary = await _qcService.GetQualityControlSummaryAsync(companyId, periodId);

        var check = summary.Checks.First(c => c.Name == "Findings and Exceptions Review");
        Assert.Equal("Attention Required", check.Status);
        Assert.False(summary.IsReadyForFinalization);
    }

    [Fact]
    public async Task GetQualityControlSummary_WithRequiresClarificationFinding_ReturnsPassAndReadyForFinalization()
    {
        // This test proves that RequiresClientClarification is EXCLUDED from unreviewed findings
        // as per the new SA 250 alignment requirement (it is considered 'reviewed' for the purpose of the initial count).
        string companyId = "COMP-QC-05";
        string periodId = "FY-2025-26";
        await SaveCompanyAsync(companyId);
        await SaveVoucherAsync(companyId);

        var state = await _finalizationService.GetOrCreateStateAsync(companyId, periodId);
        
        // Complete checklist
        var checklist = await _finalizationRepository.GetChecklistAsync(state.Id);
        foreach (var item in checklist)
        {
            await _finalizationService.SetChecklistItemCompletedAsync(item.Id, true, "Auditor-S", "Done");
        }
        state.AuditorConclusionText = "Satisfactory evidence obtained.";
        await _finalizationRepository.SaveStateAsync(state);

        // Add a finding that is RequiresClientClarification (Status = 4)
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO AuditResults (ResultId, CompanyId, RuleId, RuleName, Category, Severity, Evidence, ReviewStatus)
            VALUES (@ResultId, @CompanyId, 'R1', 'Rule 1', 1, 1, '{}', 4);
        ", new { ResultId = "EXC-QC-02", CompanyId = companyId });

        var summary = await _qcService.GetQualityControlSummaryAsync(companyId, periodId);

        var check = summary.Checks.First(c => c.Name == "Findings and Exceptions Review");
        Assert.Equal("Pass", check.Status); // Excluded from unreviewed count
        Assert.True(summary.IsReadyForFinalization); // Excluded from blockers
    }
}
