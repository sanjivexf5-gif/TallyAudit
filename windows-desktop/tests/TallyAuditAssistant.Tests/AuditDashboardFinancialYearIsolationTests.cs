using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Engine.Services;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class AuditDashboardFinancialYearIsolationTests
{
    [Fact]
    public async Task GetDashboardSummaryAsync_UsesActiveFinancialYearWhenNoPeriodIsPassed()
    {
        var start = new DateTime(2026, 4, 1);
        var end = new DateTime(2027, 3, 31);
        var repository = new Mock<IAuditRepository>();
        repository
            .Setup(x => x.GetExceptionsAsync(
                "company-1", null, null, null, 0, 10000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AuditException
                {
                    Id = "in-period",
                    CompanyId = "company-1",
                    VoucherDate = new DateTime(2026, 8, 10),
                    Severity = SeverityLevel.High
                },
                new AuditException
                {
                    Id = "prior-year",
                    CompanyId = "company-1",
                    VoucherDate = new DateTime(2025, 8, 10),
                    Severity = SeverityLevel.Critical
                }
            });
        repository
            .Setup(x => x.GetAuditRunsAsync("company-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AuditRun>());

        var context = new Mock<IActiveCompanyContext>();
        context.SetupGet(x => x.ActiveCompanyId).Returns("company-1");
        context.SetupGet(x => x.ActivePeriodFrom).Returns(start);
        context.SetupGet(x => x.ActivePeriodTo).Returns(end);
        context.SetupGet(x => x.ActiveFinancialYear).Returns("FY 2026-27");
        context.SetupGet(x => x.ActiveCompanyName).Returns("Example Company");

        var service = new AuditDashboardService(repository.Object, context.Object);
        var summary = await service.GetDashboardSummaryAsync("company-1");

        Assert.Equal(start, summary.AuditPeriodFrom);
        Assert.Equal(end, summary.AuditPeriodTo);
        Assert.Equal("FY 2026-27", summary.FinancialYear);
        Assert.Equal(1, summary.TotalFindings);
        Assert.Equal(1, summary.HighCount);
        Assert.Equal(0, summary.CriticalCount);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_PreservesExplicitCallerPeriod()
    {
        var repository = new Mock<IAuditRepository>();
        repository
            .Setup(x => x.GetExceptionsAsync(
                "company-1", null, null, null, 0, 10000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AuditException>());
        repository
            .Setup(x => x.GetAuditRunsAsync("company-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AuditRun>());

        var context = new Mock<IActiveCompanyContext>();
        context.SetupGet(x => x.ActiveCompanyId).Returns("company-1");
        context.SetupGet(x => x.ActivePeriodFrom).Returns(new DateTime(2026, 4, 1));
        context.SetupGet(x => x.ActivePeriodTo).Returns(new DateTime(2027, 3, 31));
        context.SetupGet(x => x.ActiveFinancialYear).Returns("FY 2026-27");

        var service = new AuditDashboardService(repository.Object, context.Object);
        var from = new DateTime(2026, 7, 1);
        var to = new DateTime(2026, 12, 31);
        var summary = await service.GetDashboardSummaryAsync("company-1", from, to);

        Assert.Equal(from, summary.AuditPeriodFrom);
        Assert.Equal(to, summary.AuditPeriodTo);
    }
    [Fact]
    public async Task GetDashboardSummaryAsync_SeparatesOpenReviewedAndResolvedStatuses()
    {
        var findings = new[]
        {
            new AuditException { Id = "pending", CompanyId = "company-1", Status = ReviewStatus.Pending },
            new AuditException { Id = "clarification", CompanyId = "company-1", Status = ReviewStatus.RequiresClientClarification },
            new AuditException { Id = "reviewed", CompanyId = "company-1", Status = ReviewStatus.Reviewed },
            new AuditException { Id = "resolved", CompanyId = "company-1", Status = ReviewStatus.Resolved },
            new AuditException { Id = "false-positive", CompanyId = "company-1", Status = ReviewStatus.FlaggedAsFalsePositive }
        };

        var repository = new Mock<IAuditRepository>();
        repository
            .Setup(x => x.GetExceptionsAsync(
                "company-1", null, null, null, 0, 10000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(findings);
        repository
            .Setup(x => x.GetAuditRunsAsync("company-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AuditRun>());

        var service = new AuditDashboardService(repository.Object);
        var summary = await service.GetDashboardSummaryAsync("company-1");

        Assert.Equal(2, summary.OpenFindings);
        Assert.Equal(1, summary.ReviewedFindings);
        Assert.Equal(2, summary.ResolvedFindings);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_WarnsWhenFindingPageMayBeTruncated()
    {
        var findings = Enumerable.Range(1, 10000)
            .Select(i => new AuditException
            {
                Id = $"finding-{i}",
                CompanyId = "company-1",
                RuleId = "OTHER",
                RuleName = "Other finding",
                VoucherDate = new DateTime(2026, 8, 10),
                Severity = SeverityLevel.Low
            })
            .ToArray();

        var repository = new Mock<IAuditRepository>();
        repository
            .Setup(x => x.GetExceptionsAsync(
                "company-1", null, null, null, 0, 10000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(findings);
        repository
            .Setup(x => x.GetAuditRunsAsync("company-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AuditRun>());

        var service = new AuditDashboardService(repository.Object);
        var summary = await service.GetDashboardSummaryAsync("company-1");

        Assert.Equal(10000, summary.TotalFindings);
        Assert.Contains("may be incomplete", summary.StatusMessage);
        Assert.Contains("up to 10,000 company findings", summary.StatusMessage);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_UsesCompleteDatabaseAggregatesForHeadlineMetrics()
    {
        var repository = new Mock<IAuditRepository>();
        repository
            .Setup(x => x.GetExceptionsAsync(
                "company-1", null, null, null, 0, 10000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AuditException>());
        repository
            .Setup(x => x.GetDashboardAggregatesForPeriodAsync(
                "company-1", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuditDashboardAggregates
            {
                TotalFindings = 12000,
                CriticalCount = 17,
                HighCount = 130,
                MediumCount = 1200,
                LowCount = 9000,
                InformationalCount = 1653,
                OpenFindings = 8000,
                ReviewedFindings = 2500,
                ResolvedFindings = 1500,
                TotalExceptionAmount = 1234567.89m
            });
        repository
            .Setup(x => x.GetAuditRunsAsync("company-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AuditRun>());

        var service = new AuditDashboardService(repository.Object);
        var summary = await service.GetDashboardSummaryAsync("company-1");

        Assert.Equal(12000, summary.TotalFindings);
        Assert.Equal(17, summary.CriticalCount);
        Assert.Equal(130, summary.HighCount);
        Assert.Equal(8000, summary.OpenFindings);
        Assert.Equal(2500, summary.ReviewedFindings);
        Assert.Equal(1500, summary.ResolvedFindings);
        Assert.Equal(1234567.89m, summary.TotalExceptionAmount);
        Assert.Contains("Headline counts, total exception amount, and audit-area breakdowns are complete", summary.StatusMessage);
    }

}
