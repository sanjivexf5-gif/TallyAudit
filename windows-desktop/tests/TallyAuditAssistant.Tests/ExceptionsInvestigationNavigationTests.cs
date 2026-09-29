using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.App.Services;
using TallyAuditAssistant.App.ViewModels;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class ExceptionsInvestigationNavigationTests
{
    private readonly Mock<IAuditRepository> _mockRepo;
    private readonly Mock<ISettingsService> _mockSettings;
    private readonly Mock<IActiveCompanyContext> _mockCompanyContext;
    private readonly NavigationService _navService;
    private readonly Mock<IInvestigationService> _mockInvestigationService;
    private readonly InvestigationViewModel _investigationVM;

    public ExceptionsInvestigationNavigationTests()
    {
        _mockRepo = new Mock<IAuditRepository>();
        _mockSettings = new Mock<ISettingsService>();
        _mockCompanyContext = new Mock<IActiveCompanyContext>();
        _navService = new NavigationService();

        _mockInvestigationService = new Mock<IInvestigationService>();
        _mockInvestigationService.Setup(i => i.GetOrCreateInvestigationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExceptionInvestigation
            {
                Id = "INV-001",
                ExceptionId = "EXC-101",
                CompanyId = "COMP-01",
                Status = InvestigationStatus.Open,
                RootCause = RootCauseClassification.Unknown
            });

        _mockInvestigationService.Setup(i => i.GetRelatedDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvestigationRelatedData());

        _investigationVM = new InvestigationViewModel(
            _mockInvestigationService.Object,
            _mockRepo.Object,
            _mockCompanyContext.Object,
            _navService
        );
    }

    [Fact]
    public async Task OpenInvestigationCommand_WithSelectedException_LoadsInvestigationAndNavigatesToInvestigationSection()
    {
        var vm = new ExceptionsViewModel(
            _mockRepo.Object,
            _mockSettings.Object,
            _mockCompanyContext.Object,
            _navService,
            _investigationVM,
            NullLogger<ExceptionsViewModel>.Instance
        );

        var testException = new AuditException
        {
            Id = "EXC-101",
            CompanyId = "COMP-01",
            RuleName = "Duplicate Voucher Number",
            Category = RuleCategory.DuplicateDetection,
            Severity = ExceptionSeverity.High,
            VoucherNumber = "V-9999",
            FlaggedAmount = 150000m,
            Status = ReviewStatus.Pending
        };

        vm.SelectedException = testException;

        string? navigatedSection = null;
        _navService.Navigated += (s, sec) => navigatedSection = sec;

        await vm.OpenInvestigationCommand.ExecuteAsync(null);

        Assert.Equal("Investigation", navigatedSection);
        Assert.NotNull(_investigationVM.TargetException);
        Assert.Equal("EXC-101", _investigationVM.TargetException.Id);
        Assert.Equal("COMP-01", _investigationVM.TargetException.CompanyId);
        Assert.Equal("V-9999", _investigationVM.TargetException.VoucherNumber);
        Assert.Equal(150000m, _investigationVM.TargetException.FlaggedAmount);
    }

    [Fact]
    public async Task OpenInvestigationCommand_WhenNoSelectedException_DoesNotNavigateOrThrow()
    {
        var vm = new ExceptionsViewModel(
            _mockRepo.Object,
            _mockSettings.Object,
            _mockCompanyContext.Object,
            _navService,
            _investigationVM,
            NullLogger<ExceptionsViewModel>.Instance
        );

        vm.SelectedException = null;

        string? navigatedSection = null;
        _navService.Navigated += (s, sec) => navigatedSection = sec;

        await vm.OpenInvestigationCommand.ExecuteAsync(null);

        Assert.Null(navigatedSection);
    }

    [Fact]
    public void ReturnToExceptions_NavigatesBackToExceptionsSection()
    {
        string? navigatedSection = null;
        _navService.Navigated += (s, sec) => navigatedSection = sec;

        _investigationVM.ReturnToExceptionsCommand.Execute(null);

        Assert.Equal("Exceptions", navigatedSection);
    }
}
