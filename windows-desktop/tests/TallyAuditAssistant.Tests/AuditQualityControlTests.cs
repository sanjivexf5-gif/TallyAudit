using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
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
        
        // Ensure VoucherType exists for FK
        await conn.ExecuteAsync(@"
            INSERT OR IGNORE INTO VoucherTypes (Id, CompanyId, Name, ParentType)
            VALUES ('VT-SALES', @CompanyId, 'Sales', 'Sales');
        ", new { CompanyId = companyId });

        await conn.ExecuteAsync(@"
            INSERT INTO Vouchers (Id, CompanyId, VoucherTypeId, VoucherTypeName, VoucherNumber, VoucherDate, TotalAmount, AlterId)
            VALUES (@Id, @CompanyId, 'VT-SALES', 'Sales', '1', '2025-04-01', 100, 1);
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
        Assert.NotEmpty(checklistA);
        await _finalizationService.SetChecklistItemCompletedAsync(checklistA[0].Id, true, "Auditor-S", "Complete");

        var summaryA = await _qcService.GetQualityControlSummaryAsync(compA, periodId);
        var summaryB = await _qcService.GetQualityControlSummaryAsync(compB, periodId);

        Assert.NotEqual(summaryA.PassedChecksCount, summaryB.PassedChecksCount);
    }

    private async Task EnsureRuleExistsAsync(string ruleId)
    {
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync(@"
            INSERT OR IGNORE INTO AuditRules (RuleId, Category, Name, Description, Severity, SuggestedReview, Version, IsEnabled)
            VALUES (@RuleId, 6, 'Test Rule', 'Description', 3, 'Review', '1.0.0', 1);
        ", new { RuleId = ruleId });
    }

    [Fact]
    public async Task GetQualityControlSummary_WithPendingFinding_ReturnsAttentionRequired()
    {
        string companyId = "COMP-QC-04";
        string periodId = "FY-2025-26";
        await SaveCompanyAsync(companyId);
        await SaveVoucherAsync(companyId);
        await EnsureRuleExistsAsync("ACC-DUP-01");

        var state = await _finalizationService.GetOrCreateStateAsync(companyId, periodId);

        // Add a pending finding using Dapper
        using var conn = await _factory.CreateConnectionAsync();
        await conn.ExecuteAsync(@"
            INSERT INTO Exceptions (Id, CompanyId, RuleId, RuleName, Category, Severity, EvidenceJson, Status)
            VALUES (@Id, @CompanyId, 'ACC-DUP-01', 'Duplicate Voucher Number', 6, 3, '{}', 0);
        ", new { Id = "EXC-QC-01", CompanyId = companyId });

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
        await EnsureRuleExistsAsync("ACC-DUP-01");

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
            INSERT INTO Exceptions (Id, CompanyId, RuleId, RuleName, Category, Severity, EvidenceJson, Status)
            VALUES (@Id, @CompanyId, 'ACC-DUP-01', 'Duplicate Voucher Number', 6, 3, '{}', 4);
        ", new { Id = "EXC-QC-02", CompanyId = companyId });

        var summary = await _qcService.GetQualityControlSummaryAsync(companyId, periodId);

        var check = summary.Checks.First(c => c.Name == "Findings and Exceptions Review");
        Assert.Equal("Pass", check.Status); // Excluded from unreviewed count
        Assert.True(summary.IsReadyForFinalization); // Excluded from blockers
    }

    [Fact]
    public async Task GetQualityControlSummary_CountsPendingFindingsBeyondDisplayLimit()
    {
        const string companyId = "COMP-QC-LARGE";
        const string periodId = "FY-2025-26";
        await SaveCompanyAsync(companyId);
        await SaveVoucherAsync(companyId);
        await EnsureRuleExistsAsync("ACC-DUP-01");

        using var conn = await _factory.CreateConnectionAsync();
        var resolvedRows = Enumerable.Range(0, 1001).Select(i => new
        {
            Id = $"EXC-QC-RESOLVED-{i:D4}",
            CompanyId = companyId
        });
        await conn.ExecuteAsync(@"
            INSERT INTO Exceptions
                (Id, CompanyId, RuleId, RuleName, Category, Severity, EvidenceJson, Status, FlaggedAt, ReviewedAt)
            VALUES
                (@Id, @CompanyId, 'ACC-DUP-01', 'Duplicate Voucher Number', 6, 2, '{}', 3,
                 '2026-10-09 12:00:00', '2026-10-09 12:05:00');
        ", resolvedRows);

        await conn.ExecuteAsync(@"
            INSERT INTO Exceptions
                (Id, CompanyId, RuleId, RuleName, Category, Severity, EvidenceJson, Status, FlaggedAt)
            VALUES
                ('EXC-QC-OLD-PENDING', @CompanyId, 'ACC-DUP-01', 'Duplicate Voucher Number', 6, 3, '{}', 0,
                 '2024-01-01 00:00:00');
        ", new { CompanyId = companyId });

        var summary = await _qcService.GetQualityControlSummaryAsync(companyId, periodId);
        var check = summary.Checks.First(c => c.Name == "Findings and Exceptions Review");

        Assert.Equal("Attention Required", check.Status);
        Assert.Contains("1 exceptions remain unreviewed", check.Explanation);
        Assert.False(summary.IsReadyForFinalization);
    }
}

