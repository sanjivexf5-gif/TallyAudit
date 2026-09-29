using System;
using System.IO;
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

public class AuditFinalizationTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly AuditFinalizationRepository _repository;
    private readonly Mock<IAuditTrailService> _mockAuditTrail;
    private readonly AuditFinalizationService _service;

    public AuditFinalizationTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"finalization_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance, _testDbPath);
        _repository = new AuditFinalizationRepository(_factory);
        _mockAuditTrail = new Mock<IAuditTrailService>();
        _service = new AuditFinalizationService(_repository, _mockAuditTrail.Object);
    }

    public async Task InitializeAsync()
    {
        await _initializer.InitializeAsync();
    }

    private async Task SaveCompanyAsync(string companyId)
    {
        var companyRepo = new AuditRepository(_factory);
        await companyRepo.SaveCompanyAsync(new Company
        {
            Id = companyId,
            TallyCompanyName = $"Company {companyId}",
            BooksFromDate = new DateTime(2025, 4, 1)
        });
    }

    private async Task<AuditFinalizationState> GetOrCreateStateAsync(string companyId, string financialPeriodId)
    {
        await SaveCompanyAsync(companyId);
        return await _service.GetOrCreateStateAsync(companyId, financialPeriodId);
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
        }
        catch { }
    }

    [Fact]
    public async Task GetOrCreateState_CreatesNewDraftStateAndInitializesChecklist()
    {
        string companyId = "COMP-FIN-01";
        string periodId = "FY-2025-26";

        var state = await GetOrCreateStateAsync(companyId, periodId);

        Assert.NotNull(state);
        Assert.Equal(AuditLifecycleStatus.Draft, state.Status);
        Assert.Equal(0.0, state.CompletionPercentage);

        var checklist = await _repository.GetChecklistAsync(state.Id);
        Assert.Equal(11, checklist.Count);
    }

    [Fact]
    public async Task SetChecklistItemCompleted_UpdatesPercentageAndTransitionsToInProgress()
    {
        string companyId = "COMP-FIN-02";
        string periodId = "FY-2025-26";
        var state = await GetOrCreateStateAsync(companyId, periodId);

        var checklist = await _repository.GetChecklistAsync(state.Id);
        var firstItem = checklist[0];

        await _service.SetChecklistItemCompletedAsync(firstItem.Id, true, "Auditor-S", "Document is valid");

        var updatedState = await _repository.GetStateByIdAsync(state.Id);
        Assert.NotNull(updatedState);
        Assert.Equal(AuditLifecycleStatus.InProgress, updatedState.Status);
        Assert.True(updatedState.CompletionPercentage > 0.0);
    }

    [Fact]
    public async Task InvalidTransition_ThrowsInvalidOperationException()
    {
        string companyId = "COMP-FIN-03";
        string periodId = "FY-2025-26";
        var state = await GetOrCreateStateAsync(companyId, periodId);

        // Cannot transition from Draft directly to Finalized
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            _service.UpdateStatusAsync(state.Id, AuditLifecycleStatus.Finalized, "Auditor-S"));
    }

    [Fact]
    public async Task SubmitForReview_TransitionsToReadyForReview()
    {
        string companyId = "COMP-FIN-04";
        string periodId = "FY-2025-26";
        var state = await GetOrCreateStateAsync(companyId, periodId);
        state.Status = AuditLifecycleStatus.InProgress;
        await _repository.SaveStateAsync(state);

        await _service.SubmitForReviewAsync(state.Id, "Auditor-S", "Reviewer-P");

        var updated = await _repository.GetStateByIdAsync(state.Id);
        Assert.NotNull(updated);
        Assert.Equal(AuditLifecycleStatus.ReadyForReview, updated.Status);
        Assert.Equal("Reviewer-P", updated.ReviewerName);
    }

    [Fact]
    public async Task ReturnWithNotes_TransitionsToReturned()
    {
        string companyId = "COMP-FIN-05";
        string periodId = "FY-2025-26";
        var state = await GetOrCreateStateAsync(companyId, periodId);
        state.Status = AuditLifecycleStatus.UnderReview;
        await _repository.SaveStateAsync(state);

        await _service.ReturnWithNotesAsync(state.Id, "Reviewer-P", "Please verify HSN code on invoice 05.");

        var updated = await _repository.GetStateByIdAsync(state.Id);
        Assert.NotNull(updated);
        Assert.Equal(AuditLifecycleStatus.Returned, updated.Status);
        Assert.Equal("Please verify HSN code on invoice 05.", updated.ReviewerComments);
    }

    [Fact]
    public async Task ApproveForFinalization_TransitionsToReadyForFinalization()
    {
        string companyId = "COMP-FIN-06";
        string periodId = "FY-2025-26";
        var state = await GetOrCreateStateAsync(companyId, periodId);
        state.Status = AuditLifecycleStatus.UnderReview;
        await _repository.SaveStateAsync(state);

        await _service.ApproveForFinalizationAsync(state.Id, "Reviewer-P", "All clear.");

        var updated = await _repository.GetStateByIdAsync(state.Id);
        Assert.NotNull(updated);
        Assert.Equal(AuditLifecycleStatus.ReadyForFinalization, updated.Status);
    }

    [Fact]
    public async Task FinalizeAudit_SucceedsOnlyWhenRequirementsAreMet()
    {
        string companyId = "COMP-FIN-07";
        string periodId = "FY-2025-26";
        var state = await GetOrCreateStateAsync(companyId, periodId);
        state.Status = AuditLifecycleStatus.ReadyForFinalization;
        state.AuditorConclusionText = "Unqualified Opinion";
        await _repository.SaveStateAsync(state);

        // Fails because checklist is not complete
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.FinalizeAuditAsync(state.Id, "Auditor-S"));

        // Complete all checklist items
        var checklist = await _repository.GetChecklistAsync(state.Id);
        foreach (var item in checklist)
        {
            await _service.SetChecklistItemCompletedAsync(item.Id, true, "Auditor-S", "Done");
        }

        await _service.FinalizeAuditAsync(state.Id, "Auditor-S");

        var updated = await _repository.GetStateByIdAsync(state.Id);
        Assert.NotNull(updated);
        Assert.Equal(AuditLifecycleStatus.Finalized, updated.Status);
        Assert.True(updated.IsReadOnly);
    }

    [Fact]
    public async Task ReopenAudit_CreatesAmendmentHistoryAndReopensToInProgress()
    {
        string companyId = "COMP-FIN-08";
        string periodId = "FY-2025-26";
        var state = await GetOrCreateStateAsync(companyId, periodId);
        
        // Setup finalized state
        state.Status = AuditLifecycleStatus.ReadyForFinalization;
        state.AuditorConclusionText = "Qualified";
        await _repository.SaveStateAsync(state);

        var checklist = await _repository.GetChecklistAsync(state.Id);
        foreach (var item in checklist)
        {
            await _service.SetChecklistItemCompletedAsync(item.Id, true, "Auditor-S", "Done");
        }

        await _service.FinalizeAuditAsync(state.Id, "Auditor-S");

        // Reopen
        await _service.ReopenAuditAsync(state.Id, "SeniorPartner", "New material evidence found.");

        var updated = await _repository.GetStateByIdAsync(state.Id);
        Assert.NotNull(updated);
        Assert.Equal(AuditLifecycleStatus.InProgress, updated.Status);

        var amendments = await _repository.GetAmendmentsAsync(state.Id);
        var amendment = Assert.Single(amendments);
        Assert.Equal("New material evidence found.", amendment.Reason);
        Assert.Equal("SeniorPartner", amendment.RequestedBy);
    }

    [Fact]
    public async Task Isolation_SeparateCompanyWorkflowsAreIndependent()
    {
        string compA = "COMP-A";
        string compB = "COMP-B";
        string periodId = "FY-2025-26";

        var stateA = await GetOrCreateStateAsync(compA, periodId);
        var stateB = await GetOrCreateStateAsync(compB, periodId);

        var checklistA = await _repository.GetChecklistAsync(stateA.Id);
        await _service.SetChecklistItemCompletedAsync(checklistA[0].Id, true, "Auditor-S", "Done");

        var updatedA = await _repository.GetStateByIdAsync(stateA.Id);
        var updatedB = await _repository.GetStateByIdAsync(stateB.Id);

        Assert.True(updatedA.CompletionPercentage > 0.0);
        Assert.Equal(0.0, updatedB.CompletionPercentage);
    }

    [Fact]
    public async Task AddOpenItem_PersistsAndSavesSuccessfully()
    {
        string companyId = "COMP-FIN-09";
        string periodId = "FY-2025-26";
        var state = await GetOrCreateStateAsync(companyId, periodId);

        var openItem = new OpenItem
        {
            AuditId = state.Id,
            Description = "Confirm HSN for voucher 02",
            Category = "Findings",
            Priority = "High",
            Owner = "Auditor-S",
            Status = "Open"
        };

        await _service.AddOpenItemAsync(openItem);

        var items = await _repository.GetOpenItemsAsync(state.Id);
        var savedItem = Assert.Single(items);
        Assert.Equal("Confirm HSN for voucher 02", savedItem.Description);
        Assert.Equal("High", savedItem.Priority);
    }

    [Fact]
    public async Task AddReviewNote_PersistsAndSavesSuccessfully()
    {
        string companyId = "COMP-FIN-10";
        string periodId = "FY-2025-26";
        var state = await GetOrCreateStateAsync(companyId, periodId);

        var reviewNote = new ReviewNote
        {
            AuditId = state.Id,
            Area = "GST",
            Reference = "GST-001",
            Reviewer = "Reviewer-P",
            Comment = "Need clarification on inter-state rate choice.",
            Status = "Open"
        };

        await _service.AddReviewNoteAsync(reviewNote);

        var notes = await _repository.GetReviewNotesAsync(state.Id);
        var savedNote = Assert.Single(notes);
        Assert.Equal("GST", savedNote.Area);
        Assert.Equal("Need clarification on inter-state rate choice.", savedNote.Comment);
    }
}
