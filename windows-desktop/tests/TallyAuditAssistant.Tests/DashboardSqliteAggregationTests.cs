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

public sealed class DashboardSqliteAggregationTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"dashboard_aggregate_{Guid.NewGuid():N}.db");
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
        try
        {
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
        }
        catch (IOException) { }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CategoryAmounts_UseFullCompanyPeriodScope_AndIncludeUndatedFindings()
    {
        const string companyId = "DASHBOARD-AGGREGATE-CO";
        const string otherCompanyId = "DASHBOARD-AGGREGATE-OTHER-CO";
        await _auditRepository.SaveCompanyAsync(new Company { Id = companyId, TallyCompanyName = companyId });
        await _auditRepository.SaveCompanyAsync(new Company { Id = otherCompanyId, TallyCompanyName = otherCompanyId });

        AuditResult Finding(string ruleId, string company, RuleCategory category, decimal amount, DateTime? date) => new()
        {
            CompanyId = company, RuleId = ruleId, RuleName = ruleId, Category = category,
            Severity = SeverityLevel.High, VoucherDate = date, VoucherId = ruleId, FlaggedAmount = amount,
            Explanation = "Dashboard aggregation integration test", Evidence = "{\"test\":true}"
        };

        await _resultRepository.SaveResultsBatchAsync(new[]
        {
            Finding("GST-DASH-001", companyId, RuleCategory.GST, 100m, new DateTime(2025, 5, 10)),
            Finding("GST-DASH-002", companyId, RuleCategory.GST, 25m, null),
            Finding("TDS-DASH-001", companyId, RuleCategory.TDS, 50m, new DateTime(2025, 6, 1)),
            Finding("GST-DASH-OUTSIDE", companyId, RuleCategory.GST, 900m, new DateTime(2026, 4, 1)),
            Finding("GST-DASH-OTHER", otherCompanyId, RuleCategory.GST, 700m, new DateTime(2025, 5, 10))
        });

        var totals = await _auditRepository.GetDashboardCategoryAmountsForPeriodAsync(
            companyId, new DateTime(2025, 4, 1), new DateTime(2026, 3, 31));

        Assert.Equal(125m, totals.GstExceptionAmount);
        Assert.Equal(50m, totals.TdsExceptionAmount);
    }

    [Fact]
    public async Task TopExceptionsForPeriod_IsBoundedAndExcludesOtherCompaniesAndOutOfPeriodRows()
    {
        const string companyId = "DASHBOARD-TOP-CO";
        const string otherCompanyId = "DASHBOARD-TOP-OTHER-CO";
        await _auditRepository.SaveCompanyAsync(new Company { Id = companyId, TallyCompanyName = companyId });
        await _auditRepository.SaveCompanyAsync(new Company { Id = otherCompanyId, TallyCompanyName = otherCompanyId });

        AuditResult Finding(string ruleId, string company, SeverityLevel severity, decimal amount, DateTime? date) => new()
        {
            CompanyId = company, RuleId = ruleId, RuleName = ruleId, Category = RuleCategory.GeneralAccounting,
            Severity = severity, VoucherDate = date, VoucherId = ruleId, FlaggedAmount = amount,
            Explanation = "Top findings integration test", Evidence = "{\"test\":true}"
        };

        await _resultRepository.SaveResultsBatchAsync(new[]
        {
            Finding("TOP-MEDIUM", companyId, SeverityLevel.Medium, 500m, new DateTime(2025, 7, 1)),
            Finding("TOP-HIGH", companyId, SeverityLevel.High, 100m, new DateTime(2025, 7, 2)),
            Finding("TOP-CRITICAL", companyId, SeverityLevel.Critical, 10m, new DateTime(2025, 7, 3)),
            Finding("TOP-OUTSIDE", companyId, SeverityLevel.Critical, 999m, new DateTime(2026, 4, 1)),
            Finding("TOP-OTHER", otherCompanyId, SeverityLevel.Critical, 1000m, new DateTime(2025, 7, 3)),
            Finding("TOP-UNDATED", companyId, SeverityLevel.High, 200m, null)
        });

        var top = await _auditRepository.GetTopExceptionsForPeriodAsync(
            companyId, new DateTime(2025, 4, 1), new DateTime(2026, 3, 31), limit: 2);

        Assert.Equal(2, top.Count);
        Assert.Equal("TOP-CRITICAL", top[0].RuleId);
        Assert.Equal("TOP-UNDATED", top[1].RuleId);
        Assert.DoesNotContain(top, finding => finding.RuleId is "TOP-OUTSIDE" or "TOP-OTHER");
    }

    [Fact]
    public async Task TopExceptionsForPeriod_RejectsNonPositiveLimit()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _auditRepository.GetTopExceptionsForPeriodAsync("company", null, null, limit: 0));
    }
}
