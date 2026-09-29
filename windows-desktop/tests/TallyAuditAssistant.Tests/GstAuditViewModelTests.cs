using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.App.ViewModels;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class GstAuditViewModelTests : IDisposable
{
    private readonly Mock<IAuditRepository> _mockRepository;
    private readonly Mock<ISettingsService> _mockSettingsService;
    private readonly Mock<IActiveCompanyContext> _mockCompanyContext;
    private readonly Mock<INavigationService> _mockNavigationService;

    private readonly Company _testCompanyA = new()
    {
        Id = "COMP-GST-001",
        TallyCompanyName = "Alpha Enterprises Pvt Ltd"
    };

    private readonly Company _testCompanyB = new()
    {
        Id = "COMP-GST-002",
        TallyCompanyName = "Beta Trading Corp"
    };

    public GstAuditViewModelTests()
    {
        _mockRepository = new Mock<IAuditRepository>();
        _mockSettingsService = new Mock<ISettingsService>();
        _mockCompanyContext = new Mock<IActiveCompanyContext>();
        _mockNavigationService = new Mock<INavigationService>();

        _mockCompanyContext.Setup(c => c.GetActiveCompanyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testCompanyA);
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    [Fact]
    public async Task LoadGstExceptionsAsync_WhenNoFindings_SetsEmptyStateAndNullSelection()
    {
        // Arrange
        _mockRepository.Setup(r => r.GetExceptionsFilteredAsync(
                _testCompanyA.Id, "GST", "All", "All", null, "Priority", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AuditException>());

        using var vm = new GstAuditViewModel(
            _mockRepository.Object,
            _mockSettingsService.Object,
            _mockCompanyContext.Object,
            _mockNavigationService.Object,
            NullLogger<GstAuditViewModel>.Instance);

        // Act
        await vm.LoadGstExceptionsAsync();

        // Assert
        Assert.Empty(vm.Exceptions);
        Assert.False(vm.HasExceptions);
        Assert.Null(vm.SelectedException);
        Assert.Equal(_testCompanyA.TallyCompanyName, vm.ActiveCompanyName);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task LoadGstExceptionsAsync_WhenSingleFinding_SetsHasExceptionsAndSelectsFirst()
    {
        // Arrange
        var finding = new AuditException
        {
            Id = "EXC-GST-001",
            CompanyId = _testCompanyA.Id,
            RuleName = "GST Rate Mismatch",
            Category = RuleCategory.GST,
            Severity = SeverityLevel.High,
            Status = ReviewStatus.Pending,
            VoucherNumber = "VCH-1001",
            FlaggedAmount = 18000m,
            SuggestedCorrection = "Verify tax ledger rate applicability."
        };

        _mockRepository.Setup(r => r.GetExceptionsFilteredAsync(
                _testCompanyA.Id, "GST", "All", "All", null, "Priority", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { finding });

        using var vm = new GstAuditViewModel(
            _mockRepository.Object,
            _mockSettingsService.Object,
            _mockCompanyContext.Object,
            _mockNavigationService.Object,
            NullLogger<GstAuditViewModel>.Instance);

        // Act
        await vm.LoadGstExceptionsAsync();

        // Assert
        Assert.Single(vm.Exceptions);
        Assert.True(vm.HasExceptions);
        Assert.NotNull(vm.SelectedException);
        Assert.Equal("EXC-GST-001", vm.SelectedException!.Id);
        Assert.Equal("VCH-1001", vm.SelectedException.VoucherNumber);
        Assert.Equal(18000m, vm.SelectedException.FlaggedAmount);
    }

    [Fact]
    public async Task LoadGstExceptionsAsync_WhenTransitioningFromFindingsToZero_ClearsStaleSelection()
    {
        // Arrange
        var finding = new AuditException
        {
            Id = "EXC-GST-STALE",
            CompanyId = _testCompanyA.Id,
            RuleName = "Blocked ITC on Motor Vehicles",
            Category = RuleCategory.GST,
            Severity = SeverityLevel.Critical,
            Status = ReviewStatus.Pending,
            VoucherNumber = "VCH-8888",
            FlaggedAmount = 50000m
        };

        _mockRepository.SetupSequence(r => r.GetExceptionsFilteredAsync(
                _testCompanyA.Id, "GST", "All", "All", null, "Priority", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { finding })
            .ReturnsAsync(Array.Empty<AuditException>());

        using var vm = new GstAuditViewModel(
            _mockRepository.Object,
            _mockSettingsService.Object,
            _mockCompanyContext.Object,
            _mockNavigationService.Object,
            NullLogger<GstAuditViewModel>.Instance);

        // Act 1: Initial load has 1 finding
        await vm.LoadGstExceptionsAsync();
        Assert.NotNull(vm.SelectedException);
        Assert.Equal("EXC-GST-STALE", vm.SelectedException!.Id);
        Assert.True(vm.HasExceptions);

        // Act 2: Subsequent refresh returns 0 findings
        await vm.LoadGstExceptionsAsync();

        // Assert
        Assert.Empty(vm.Exceptions);
        Assert.False(vm.HasExceptions);
        Assert.Null(vm.SelectedException);
        Assert.Equal(string.Empty, vm.AuditorNoteInput);
    }

    [Fact]
    public async Task LoadGstExceptionsAsync_OverlappingCalls_OnlyLatestGenerationUpdatesState()
    {
        // Arrange
        var initialResult = Array.Empty<AuditException>();
        var fastResult = new[]
        {
            new AuditException { Id = "FAST-1", CompanyId = _testCompanyA.Id, RuleName = "Fast Finding", Category = RuleCategory.GST, Status = ReviewStatus.Pending }
        };

        var slowResult = new[]
        {
            new AuditException { Id = "SLOW-1", CompanyId = _testCompanyA.Id, RuleName = "Slow Finding", Category = RuleCategory.GST, Status = ReviewStatus.Pending }
        };

        var slowTcs = new TaskCompletionSource<IReadOnlyList<AuditException>>();
        var fastTcs = new TaskCompletionSource<IReadOnlyList<AuditException>>();

        // Start with initial setup returning empty list for constructor
        _mockRepository.Setup(r => r.GetExceptionsFilteredAsync(
                _testCompanyA.Id, "GST", "All", "All", null, "Priority", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialResult);

        using var vm = new GstAuditViewModel(
            _mockRepository.Object,
            _mockSettingsService.Object,
            _mockCompanyContext.Object,
            _mockNavigationService.Object,
            NullLogger<GstAuditViewModel>.Instance);

        await vm.LoadGstExceptionsAsync(); // ensure constructor run is settled

        // Configure mock sequence for overlapping calls: first call gets slowTcs, second gets fastTcs
        int callCount = 0;
        _mockRepository.Setup(r => r.GetExceptionsFilteredAsync(
                _testCompanyA.Id, "GST", "All", "All", null, "Priority", true, It.IsAny<CancellationToken>()))
            .Returns((string c, string cat, string sev, string stat, string q, string sb, bool desc, CancellationToken ct) =>
            {
                int count = Interlocked.Increment(ref callCount);
                if (count == 1)
                {
                    return slowTcs.Task;
                }
                return fastTcs.Task;
            });

        // Act: trigger call 1 (slow), then call 2 (fast)
        var task1 = vm.LoadGstExceptionsAsync();
        var task2 = vm.LoadGstExceptionsAsync();

        // Complete fast (call 2) then complete slow (call 1)
        fastTcs.SetResult(fastResult);
        slowTcs.SetResult(slowResult);

        await Task.WhenAll(task1, task2);

        // Assert: only the second (newest generation) result should remain
        Assert.Single(vm.Exceptions);
        Assert.Equal("FAST-1", vm.SelectedException?.Id);
    }

    [Fact]
    public async Task OnActiveCompanyChanged_WhileLoadInProgress_UpdatesToNewCompanyResultsOnly()
    {
        // Arrange
        var companyAResult = new[]
        {
            new AuditException { Id = "EXC-A", CompanyId = _testCompanyA.Id, RuleName = "Alpha Finding", Category = RuleCategory.GST, Status = ReviewStatus.Pending }
        };

        var companyBResult = new[]
        {
            new AuditException { Id = "EXC-B", CompanyId = _testCompanyB.Id, RuleName = "Beta Finding", Category = RuleCategory.GST, Status = ReviewStatus.Pending }
        };

        _mockRepository.Setup(r => r.GetExceptionsFilteredAsync(
                _testCompanyA.Id, "GST", "All", "All", null, "Priority", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(companyAResult);

        _mockRepository.Setup(r => r.GetExceptionsFilteredAsync(
                _testCompanyB.Id, "GST", "All", "All", null, "Priority", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(companyBResult);

        using var vm = new GstAuditViewModel(
            _mockRepository.Object,
            _mockSettingsService.Object,
            _mockCompanyContext.Object,
            _mockNavigationService.Object,
            NullLogger<GstAuditViewModel>.Instance);

        await vm.LoadGstExceptionsAsync();
        Assert.Equal("EXC-A", vm.SelectedException?.Id);
        Assert.Equal(_testCompanyA.TallyCompanyName, vm.ActiveCompanyName);

        // Switch company to Company B
        _mockCompanyContext.Setup(c => c.GetActiveCompanyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testCompanyB);

        _mockCompanyContext.Raise(c => c.ActiveCompanyChanged += null, this, _testCompanyB);

        // Wait for load to finish
        await vm.LoadGstExceptionsAsync();

        // Assert: Company B is active and its finding is selected
        Assert.Equal(_testCompanyB.TallyCompanyName, vm.ActiveCompanyName);
        Assert.Single(vm.Exceptions);
        Assert.Equal("EXC-B", vm.SelectedException?.Id);
    }

    [Fact]
    public async Task RepeatedRefreshClicks_DoNotThrowOrCorruptState()
    {
        // Arrange
        var findings = new List<AuditException>
        {
            new() { Id = "EXC-1", CompanyId = _testCompanyA.Id, RuleName = "GST 1", Category = RuleCategory.GST, Status = ReviewStatus.Pending },
            new() { Id = "EXC-2", CompanyId = _testCompanyA.Id, RuleName = "GST 2", Category = RuleCategory.GST, Status = ReviewStatus.Pending }
        };

        _mockRepository.Setup(r => r.GetExceptionsFilteredAsync(
                _testCompanyA.Id, "GST", "All", "All", null, "Priority", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(findings);

        using var vm = new GstAuditViewModel(
            _mockRepository.Object,
            _mockSettingsService.Object,
            _mockCompanyContext.Object,
            _mockNavigationService.Object,
            NullLogger<GstAuditViewModel>.Instance);

        // Act: spam 10 concurrent refresh calls
        var tasks = Enumerable.Range(0, 10).Select(_ => vm.LoadGstExceptionsAsync()).ToArray();
        await Task.WhenAll(tasks);

        // Assert: state is completely clean and valid
        Assert.Equal(2, vm.Exceptions.Count);
        Assert.NotNull(vm.SelectedException);
        Assert.Equal("EXC-1", vm.SelectedException!.Id);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task LoadGstExceptionsAsync_WhenNoCompanySelected_ClearsAllFindingsAndFlags()
    {
        // Arrange
        _mockCompanyContext.Setup(c => c.GetActiveCompanyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Company?)null);

        using var vm = new GstAuditViewModel(
            _mockRepository.Object,
            _mockSettingsService.Object,
            _mockCompanyContext.Object,
            _mockNavigationService.Object,
            NullLogger<GstAuditViewModel>.Instance);

        // Act
        await vm.LoadGstExceptionsAsync();

        // Assert
        Assert.Equal("No Company Selected", vm.ActiveCompanyName);
        Assert.Empty(vm.Exceptions);
        Assert.False(vm.HasExceptions);
        Assert.Null(vm.SelectedException);
    }

    [Fact]
    public async Task MarkAsAccepted_And_MarkAsReviewed_UpdateReviewStatusLocally()
    {
        // Arrange
        var finding = new AuditException
        {
            Id = "EXC-GST-ACTIONS",
            CompanyId = _testCompanyA.Id,
            RuleName = "Place of Supply Inconsistency",
            Category = RuleCategory.GST,
            Status = ReviewStatus.Pending
        };

        _mockRepository.Setup(r => r.GetExceptionsFilteredAsync(
                _testCompanyA.Id, "GST", "All", "All", null, "Priority", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { finding });

        using var vm = new GstAuditViewModel(
            _mockRepository.Object,
            _mockSettingsService.Object,
            _mockCompanyContext.Object,
            _mockNavigationService.Object,
            NullLogger<GstAuditViewModel>.Instance);

        await vm.LoadGstExceptionsAsync();
        Assert.NotNull(vm.SelectedException);

        vm.AuditorNoteInput = "Verified tax invoice manually.";

        // Act: Mark Reviewed
        await vm.MarkAsReviewedCommand.ExecuteAsync(null);

        // Verify repository was called to update status to Reviewed
        _mockRepository.Verify(r => r.UpdateExceptionStatusAsync(
            "EXC-GST-ACTIONS",
            ReviewStatus.Reviewed,
            "Verified tax invoice manually.",
            It.IsAny<CancellationToken>()), Times.Once);

        // Act: Mark Accepted / Resolved
        await vm.MarkAsAcceptedCommand.ExecuteAsync(null);

        // Verify repository was called to update status to Resolved
        _mockRepository.Verify(r => r.UpdateExceptionStatusAsync(
            "EXC-GST-ACTIONS",
            ReviewStatus.Resolved,
            "Verified tax invoice manually.",
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
