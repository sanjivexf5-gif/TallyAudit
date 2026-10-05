using System;
using System.IO;
using System.Threading;
using System.Linq;
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

public class InvestigationServiceTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly InvestigationRepository _investigationRepository;
    private readonly AuditRepository _auditRepository;
    private readonly AuditFinalizationRepository _finalizationRepository;
    private readonly Mock<IAuditTrailService> _mockAuditTrail;
    private readonly InvestigationService _service;

    public InvestigationServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"investigation_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance, _testDbPath);
        _investigationRepository = new InvestigationRepository(_factory, NullLogger<InvestigationRepository>.Instance);
        _auditRepository = new AuditRepository(_factory);
        _finalizationRepository = new AuditFinalizationRepository(_factory);
        _mockAuditTrail = new Mock<IAuditTrailService>();
        _mockAuditTrail.Setup(a => a.GetAuditTrailAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AuditTrailEntry>());

        _service = new InvestigationService(
            _investigationRepository,
            _auditRepository,
            _mockAuditTrail.Object,
            _finalizationRepository,
            NullLogger<InvestigationService>.Instance
        );
    }

    public async Task InitializeAsync()
    {
        await _initializer.InitializeAsync();
    }

    private async Task SeedCompanyAndExceptionAsync(string companyId, string exceptionId)
    {
        await _auditRepository.SaveCompanyAsync(new Company
        {
            Id = companyId,
            TallyCompanyName = $"Test Company {companyId}",
            BooksFromDate = new DateTime(2025, 4, 1)
        });

        // Insert rule and exception into database
        using var conn = await _factory.CreateConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR IGNORE INTO AuditRules (RuleId, Category, Name, Description, Severity, SuggestedReview, Version, IsEnabled)
            VALUES ('ACC-DUP-01', 6, 'Duplicate Voucher Number', 'Duplicate check', 3, 'Inspect duplicate', '1.0.0', 1);

            INSERT OR REPLACE INTO Exceptions (
                Id, CompanyId, RuleId, RuleName, Category, Severity, VoucherNumber, FlaggedAmount, Status, EvidenceJson
            ) VALUES (
                $ExceptionId, $CompanyId, 'ACC-DUP-01', 'Duplicate Voucher Number', 6, 3, 'V-1001', 50000.0, 0, '{}'
            );";
        cmd.Parameters.AddWithValue("$ExceptionId", exceptionId);
        cmd.Parameters.AddWithValue("$CompanyId", companyId);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task GetOrCreateInvestigationAsync_CreatesInvestigationWith15DefaultChecklistItems()
    {
        const string compId = "COMP-INV-01";
        const string excId = "EXC-INV-01";
        await SeedCompanyAndExceptionAsync(compId, excId);

        var inv = await _service.GetOrCreateInvestigationAsync(excId, compId, "AuditorA");

        Assert.NotNull(inv);
        Assert.Equal(excId, inv.ExceptionId);
        Assert.Equal(compId, inv.CompanyId);
        Assert.Equal(InvestigationStatus.Open, inv.Status);
        Assert.Equal(RootCauseClassification.Unknown, inv.RootCause);
        Assert.Equal(15, inv.ChecklistItems.Count);
        Assert.Contains(inv.ChecklistItems, c => c.Code == "INV-CHK-01" && c.Description == "Review source transaction");
        Assert.Contains(inv.ChecklistItems, c => c.Code == "INV-CHK-15" && c.Description == "Record conclusion");

        _mockAuditTrail.Verify(a => a.RecordActivityAsync(
            "InvestigationCreated",
            "INVESTIGATION",
            It.IsAny<string>(),
            "ExceptionInvestigation",
            inv.Id,
            null,
            It.IsAny<string>(),
            null,
            compId,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrCreateInvestigationAsync_WhenAlreadyExists_ReturnsSameInstance()
    {
        const string compId = "COMP-INV-02";
        const string excId = "EXC-INV-02";
        await SeedCompanyAndExceptionAsync(compId, excId);

        var inv1 = await _service.GetOrCreateInvestigationAsync(excId, compId, "AuditorA");
        var inv2 = await _service.GetOrCreateInvestigationAsync(excId, compId, "AuditorB");

        Assert.Equal(inv1.Id, inv2.Id);
        Assert.Equal(15, inv2.ChecklistItems.Count);
    }

    [Theory]
    [InlineData(InvestigationStatus.Open, InvestigationStatus.Investigating, true)]
    [InlineData(InvestigationStatus.Open, InvestigationStatus.Resolved, false)]
    [InlineData(InvestigationStatus.Open, InvestigationStatus.Accepted, false)]
    [InlineData(InvestigationStatus.Investigating, InvestigationStatus.AwaitingEvidence, true)]
    [InlineData(InvestigationStatus.Investigating, InvestigationStatus.AwaitingManagementResponse, true)]
    [InlineData(InvestigationStatus.Investigating, InvestigationStatus.Resolved, true)]
    [InlineData(InvestigationStatus.Investigating, InvestigationStatus.NotResolved, true)]
    [InlineData(InvestigationStatus.Investigating, InvestigationStatus.Escalated, true)]
    [InlineData(InvestigationStatus.AwaitingEvidence, InvestigationStatus.Investigating, true)]
    [InlineData(InvestigationStatus.AwaitingEvidence, InvestigationStatus.Resolved, true)]
    [InlineData(InvestigationStatus.Resolved, InvestigationStatus.Investigating, true)]
    [InlineData(InvestigationStatus.Resolved, InvestigationStatus.AwaitingEvidence, false)]
    public void CanTransition_ValidatesAllowedTransitionsCorrectly(
        InvestigationStatus current, 
        InvestigationStatus target, 
        bool expectedAllowed)
    {
        bool allowed = _service.CanTransition(current, target);
        Assert.Equal(expectedAllowed, allowed);
    }

    [Fact]
    public async Task TransitionStatusAsync_ValidTransitions_UpdatesStatusAndRecordsAuditTrail()
    {
        const string compId = "COMP-INV-03";
        const string excId = "EXC-INV-03";
        await SeedCompanyAndExceptionAsync(compId, excId);

        var inv = await _service.GetOrCreateInvestigationAsync(excId, compId, "AuditorA");

        // Open -> Investigating
        await _service.TransitionStatusAsync(inv.Id, InvestigationStatus.Investigating, "AuditorA", "Starting audit investigation");
        var updated = await _service.GetInvestigationByIdAsync(inv.Id);
        Assert.NotNull(updated);
        Assert.Equal(InvestigationStatus.Investigating, updated.Status);
        Assert.Null(updated.ClosedAt);

        // Investigating -> AwaitingEvidence
        await _service.TransitionStatusAsync(inv.Id, InvestigationStatus.AwaitingEvidence, "AuditorA", "Requested vendor bill");
        updated = await _service.GetInvestigationByIdAsync(inv.Id);
        Assert.NotNull(updated);
        Assert.Equal(InvestigationStatus.AwaitingEvidence, updated.Status);

        // AwaitingEvidence -> Investigating
        await _service.TransitionStatusAsync(inv.Id, InvestigationStatus.Investigating, "AuditorA", "Evidence received");
        updated = await _service.GetInvestigationByIdAsync(inv.Id);
        Assert.NotNull(updated);
        Assert.Equal(InvestigationStatus.Investigating, updated.Status);

        // Investigating -> Resolved
        await _service.TransitionStatusAsync(inv.Id, InvestigationStatus.Resolved, "AuditorA", "Confirmed and rectified");
        updated = await _service.GetInvestigationByIdAsync(inv.Id);
        Assert.NotNull(updated);
        Assert.Equal(InvestigationStatus.Resolved, updated.Status);
        Assert.NotNull(updated.ClosedAt);
    }

    [Fact]
    public async Task TransitionStatusAsync_InvalidTransition_ThrowsInvalidOperationException()
    {
        const string compId = "COMP-INV-04";
        const string excId = "EXC-INV-04";
        await SeedCompanyAndExceptionAsync(compId, excId);

        var inv = await _service.GetOrCreateInvestigationAsync(excId, compId, "AuditorA");

        // Attempt invalid jump: Open directly to Resolved
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.TransitionStatusAsync(inv.Id, InvestigationStatus.Resolved, "AuditorA")
        );
    }

    [Fact]
    public async Task TransitionStatusAsync_ClosedAt_IsClearedWhenReopened()
    {
        const string compId = "COMP-INV-05";
        const string excId = "EXC-INV-05";
        await SeedCompanyAndExceptionAsync(compId, excId);

        var inv = await _service.GetOrCreateInvestigationAsync(excId, compId, "AuditorA");
        await _service.TransitionStatusAsync(inv.Id, InvestigationStatus.Investigating, "AuditorA");
        await _service.TransitionStatusAsync(inv.Id, InvestigationStatus.Resolved, "AuditorA");

        var resolvedInv = await _service.GetInvestigationByIdAsync(inv.Id);
        Assert.NotNull(resolvedInv);
        Assert.NotNull(resolvedInv.ClosedAt);

        // Reopen to Investigating
        await _service.TransitionStatusAsync(inv.Id, InvestigationStatus.Investigating, "AuditorA", "Re-opening for supplementary test");
        var reopenedInv = await _service.GetInvestigationByIdAsync(inv.Id);
        Assert.NotNull(reopenedInv);
        Assert.Null(reopenedInv.ClosedAt);
    }

    [Fact]
    public async Task UpdateInvestigationAsync_UpdatesRootCauseAndNotes()
    {
        const string compId = "COMP-INV-06";
        const string excId = "EXC-INV-06";
        await SeedCompanyAndExceptionAsync(compId, excId);

        var inv = await _service.GetOrCreateInvestigationAsync(excId, compId, "AuditorA");
        inv.RootCause = RootCauseClassification.DataEntry;
        inv.AuditorNotes = "Typographical error in voucher reference entered by accounts clerk.";
        inv.ManagementResponse = "Acknowledged; clerk retrained on numbering format.";
        inv.ProposedCorrectiveAction = "Amend voucher reference in Tally.";
        inv.ReviewerNotes = "Auditor reasoning verified and approved.";

        await _service.UpdateInvestigationAsync(inv, "AuditorA");

        var updated = await _service.GetInvestigationByIdAsync(inv.Id);
        Assert.NotNull(updated);
        Assert.Equal(RootCauseClassification.DataEntry, updated.RootCause);
        Assert.Equal("Typographical error in voucher reference entered by accounts clerk.", updated.AuditorNotes);
        Assert.Equal("Acknowledged; clerk retrained on numbering format.", updated.ManagementResponse);
        Assert.Equal("Amend voucher reference in Tally.", updated.ProposedCorrectiveAction);
        Assert.Equal("Auditor reasoning verified and approved.", updated.ReviewerNotes);
    }

    [Fact]
    public async Task ToggleChecklistItemAsync_UpdatesCompletionAndAuditorNotes()
    {
        const string compId = "COMP-INV-07";
        const string excId = "EXC-INV-07";
        await SeedCompanyAndExceptionAsync(compId, excId);

        var inv = await _service.GetOrCreateInvestigationAsync(excId, compId, "AuditorA");
        var firstItem = inv.ChecklistItems.First();

        await _service.ToggleChecklistItemAsync(firstItem.Id, true, "AuditorA", "Inspected journal batch #401");

        var items = await _investigationRepository.GetChecklistItemsAsync(inv.Id);
        var updatedItem = items.First(i => i.Id == firstItem.Id);

        Assert.True(updatedItem.IsCompleted);
        Assert.NotNull(updatedItem.CompletedAt);
        Assert.Equal("AuditorA", updatedItem.CompletedBy);
        Assert.Equal("Inspected journal batch #401", updatedItem.Notes);
    }

    [Fact]
    public async Task GetRelatedDataAsync_ReturnsVoucherAndAuditContext()
    {
        const string compId = "COMP-INV-08";
        const string excId = "EXC-INV-08";
        await SeedCompanyAndExceptionAsync(compId, excId);

        var relatedData = await _service.GetRelatedDataAsync(excId, compId);

        Assert.NotNull(relatedData);
        Assert.NotNull(relatedData.SourceVoucher);
        Assert.Equal("V-1001", relatedData.SourceVoucher.VoucherNumber);
        Assert.Equal(50000.0m, relatedData.SourceVoucher.TotalAmount);
    }

    [Fact]
    public async Task SaveConclusionAsync_PersistsAuditorConclusionAndNotes()
    {
        const string compId = "COMP-INV-09";
        const string excId = "EXC-INV-09";
        await SeedCompanyAndExceptionAsync(compId, excId);

        var inv = await _service.GetOrCreateInvestigationAsync(excId, compId, "AuditorA");
        await _service.SaveConclusionAsync(inv.Id, InvestigationConclusion.ExceptionConfirmed, "Auditor verified duplicate entry.", "AuditorA");

        var updated = await _service.GetInvestigationByIdAsync(inv.Id);
        Assert.NotNull(updated);
        Assert.Equal(InvestigationConclusion.ExceptionConfirmed, updated.Conclusion);
        Assert.Equal("Auditor verified duplicate entry.", updated.ConclusionNotes);
    }

    [Fact]
    public async Task UpdateInvestigationAsync_PersistsRecurrenceEvidenceAndWorkingPapers()
    {
        const string compId = "COMP-INV-10";
        const string excId = "EXC-INV-10";
        await SeedCompanyAndExceptionAsync(compId, excId);

        var inv = await _service.GetOrCreateInvestigationAsync(excId, compId, "AuditorA");
        inv.RecurrenceStatus = RecurrenceClassification.Recurring;
        inv.LinkedEvidenceIds = "EVD-001,EVD-002";
        inv.LinkedWorkingPaperIds = "WP-2026-GST-01";
        inv.FinancialPeriodId = "FY-2025-26";

        await _service.UpdateInvestigationAsync(inv, "AuditorA");

        var updated = await _service.GetInvestigationByIdAsync(inv.Id);
        Assert.NotNull(updated);
        Assert.Equal(RecurrenceClassification.Recurring, updated.RecurrenceStatus);
        Assert.Equal("EVD-001,EVD-002", updated.LinkedEvidenceIds);
        Assert.Equal("WP-2026-GST-01", updated.LinkedWorkingPaperIds);
        Assert.Equal("FY-2025-26", updated.FinancialPeriodId);
    }

    [Fact]
    public async Task CompanyAndPeriodIsolation_EnsuresIndependentInvestigations()
    {
        const string compA = "COMP-ISOLATION-A";
        const string compB = "COMP-ISOLATION-B";
        const string excA = "EXC-ISOLATION-A";
        const string excB = "EXC-ISOLATION-B";

        await SeedCompanyAndExceptionAsync(compA, excA);
        await SeedCompanyAndExceptionAsync(compB, excB);

        var invA = await _service.GetOrCreateInvestigationAsync(excA, compA, "AuditorA");
        var invB = await _service.GetOrCreateInvestigationAsync(excB, compB, "AuditorB");

        Assert.NotEqual(invA.Id, invB.Id);
        Assert.Equal(compA, invA.CompanyId);
        Assert.Equal(compB, invB.CompanyId);
    }

    [Fact]
    public void InvestigationConclusion_EnumValues_ContainAllAuditorChoices()
    {
        var values = Enum.GetValues(typeof(InvestigationConclusion)).Cast<InvestigationConclusion>().ToList();
        Assert.Contains(InvestigationConclusion.Pending, values);
        Assert.Contains(InvestigationConclusion.NoExceptionNoted, values);
        Assert.Contains(InvestigationConclusion.ExceptionConfirmed, values);
        Assert.Contains(InvestigationConclusion.FurtherReviewRequired, values);
        Assert.Contains(InvestigationConclusion.UnableToComplete, values);
        Assert.Contains(InvestigationConclusion.NotApplicable, values);
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch
        {
            // Ignore temp file cleanup exceptions
        }
        await Task.CompletedTask;
    }
}
