using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Sync;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Engine.Services;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class AuditAutomationServiceTests
{
    [Fact]
    public async Task RunAsync_WhenAnyAuditRuleFails_ReturnsIncompleteAndPreservesFindings()
    {
        var failure = new AuditRuleFailure(
            "ACC-FAIL-01",
            "Simulated rule",
            "Simulated database query failure.");
        var finding = new AuditResult
        {
            ResultId = "finding-1",
            CompanyId = "company-1",
            RuleId = "ACC-FOUND-01",
            RuleName = "Existing finding"
        };
        var (service, auditEngine) = CreateService(
            new[] { failure },
            new[] { finding });

        var result = await service.RunAsync(
            runIncrementalSync: false,
            runFullAudit: true);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsIncomplete);
        Assert.Equal(1, result.FindingsGenerated);
        Assert.Contains("AUDIT INCOMPLETE", result.ErrorMessage);
        Assert.Contains("ACC-FAIL-01", result.ErrorMessage);
        Assert.Equal(AuditAutomationStage.Incomplete, service.CurrentStage);
        auditEngine.Verify(
            engine => engine.ExecuteAuditAsync(
                It.IsAny<AuditExecutionContext>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenAllAuditRulesSucceed_ReturnsSuccess()
    {
        var finding = new AuditResult
        {
            ResultId = "finding-2",
            CompanyId = "company-1",
            RuleId = "ACC-FOUND-02",
            RuleName = "Existing finding"
        };
        var (service, _) = CreateService(
            Array.Empty<AuditRuleFailure>(),
            new[] { finding });

        var result = await service.RunAsync(
            runIncrementalSync: false,
            runFullAudit: true);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsIncomplete);
        Assert.Equal(1, result.FindingsGenerated);
        Assert.Equal(AuditAutomationStage.Completed, service.CurrentStage);
    }

    private static (AuditAutomationService Service, Mock<IAuditEngine> AuditEngine) CreateService(
        IReadOnlyList<AuditRuleFailure> failures,
        IReadOnlyList<AuditResult> findings)
    {
        var connection = new Mock<ITallyConnection>();
        connection.SetupGet(x => x.ActiveEndpoint)
            .Returns(new TallyEndpointInfo("localhost", 9000, true));

        var company = new Company
        {
            Id = "company-1",
            TallyCompanyName = "Test Company",
            BooksFromDate = new DateTime(2025, 4, 1)
        };
        var companyContext = new Mock<IActiveCompanyContext>();
        companyContext.Setup(x => x.EnsureAndInitializeActiveCompanyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(company);
        companyContext.Setup(x => x.GetActivePeriodAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinancialPeriod
            {
                CompanyId = company.Id,
                StartDate = new DateTime(2025, 4, 1),
                EndDate = new DateTime(2026, 3, 31)
            });
        companyContext.SetupGet(x => x.ActiveCompanyName).Returns(company.TallyCompanyName);

        var syncManager = new Mock<ISyncManager>();
        syncManager.SetupGet(x => x.CurrentMetrics).Returns(new SyncMetrics { RecordsProcessed = 17 });

        var auditEngine = new Mock<IAuditEngine>();
        auditEngine.Setup(x => x.ExecuteAuditAsync(
                It.IsAny<AuditExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(findings);
        auditEngine.SetupGet(x => x.LastExecutionFailures).Returns(failures);

        var service = new AuditAutomationService(
            connection.Object,
            companyContext.Object,
            syncManager.Object,
            auditEngine.Object,
            NullLogger<AuditAutomationService>.Instance);

        return (service, auditEngine);
    }
}
