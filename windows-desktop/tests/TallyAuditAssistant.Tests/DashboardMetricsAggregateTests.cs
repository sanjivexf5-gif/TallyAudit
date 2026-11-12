using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class DashboardMetricsAggregateTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"dashboard_metrics_{Guid.NewGuid():N}.db");
    private SqliteConnectionFactory _factory = null!;
    private DatabaseInitializer _initializer = null!;
    private AuditRepository _auditRepository = null!;
    private AuditResultRepository _resultRepository = null!;

    public async Task InitializeAsync()
    {
        _factory = new SqliteConnectionFactory(_databasePath);
        _initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance, _databasePath);
        _auditRepository = new AuditRepository(_factory);
        _resultRepository = new AuditResultRepository(_factory, NullLogger<AuditResultRepository>.Instance);
        await _initializer.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        try { if (File.Exists(_databasePath)) File.Delete(_databasePath); }
        catch (IOException) { }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DashboardAggregates_ReturnExactCompanyWideCategoryStatusAndReconciliationCounts()
    {
        const string companyId = "DASHBOARD-COUNTS-CO";
        const string otherCompanyId = "DASHBOARD-COUNTS-OTHER";
        await _auditRepository.SaveCompanyAsync(new Company { Id = companyId, TallyCompanyName = companyId });
        await _auditRepository.SaveCompanyAsync(new Company { Id = otherCompanyId, TallyCompanyName = otherCompanyId });

        AuditResult Finding(string ruleId, string company, RuleCategory category, SeverityLevel severity, ReviewStatus status) => new()
        {
            CompanyId = company, RuleId = ruleId, RuleName = ruleId, Category = category,
            Severity = severity, Status = status, VoucherId = ruleId, FlaggedAmount = 10m,
            Explanation = "Dashboard aggregate integration test", Evidence = "{}"
        };

        await _resultRepository.SaveResultsBatchAsync(new[]
        {
            Finding("GST-COUNT-1", companyId, RuleCategory.GST, SeverityLevel.High, ReviewStatus.Pending),
            Finding("TDS-COUNT-1", companyId, RuleCategory.TDS, SeverityLevel.Medium, ReviewStatus.Reviewed),
            Finding("DUP-COUNT-1", companyId, RuleCategory.DuplicateDetection, SeverityLevel.Low, ReviewStatus.RequiresClientClarification),
            Finding("REC-COUNT-1", companyId, RuleCategory.GeneralAccounting, SeverityLevel.High, ReviewStatus.Pending),
            Finding("REC-COUNT-2", companyId, RuleCategory.GeneralAccounting, SeverityLevel.Low, ReviewStatus.Resolved),
            Finding("GST-COUNT-OTHER", otherCompanyId, RuleCategory.GST, SeverityLevel.Critical, ReviewStatus.Pending)
        });

        var totals = await _auditRepository.GetDashboardAggregatesForPeriodAsync(companyId, null, null, asOfDate: DateTime.Today);

        Assert.Equal(1, totals.GstFindings);
        Assert.Equal(1, totals.TdsFindings);
        Assert.Equal(1, totals.DuplicateDetectionFindings);
        Assert.Equal(2, totals.GeneralAccountingFindings);
        Assert.Equal(2, totals.HighPriorityFindings);
        Assert.Equal(2, totals.PendingFindings);
        Assert.Equal(1, totals.ReviewedFindings);
        Assert.Equal(1, totals.ResolvedFindings);
        Assert.Equal(1, totals.ResolvedOnlyFindings);
        Assert.Equal(1, totals.ClarificationFindings);
        Assert.Equal(2, totals.ReconciliationDifferences);
        Assert.Equal(1, totals.ReconciliationWarnings);
        Assert.Equal(2, totals.ReconciliationRuleCount);
    }
}
