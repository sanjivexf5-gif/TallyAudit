using System;
using System.Linq;
using TallyAuditAssistant.Engine.Tds.Returns;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class TcsComplianceDeadlineServiceTests
{
    private readonly TcsComplianceDeadlineService _service = new();

    [Theory]
    [InlineData(2026, 1, 12, 2026, 2, 7)]
    [InlineData(2026, 3, 31, 2026, 4, 30)]
    [InlineData(2026, 4, 1, 2026, 5, 7)]
    public void GetDepositDueDate_AppliesMonthlyAndMarchRules(int y, int m, int d, int dueY, int dueM, int dueD)
    {
        Assert.Equal(new DateOnly(dueY, dueM, dueD),
            _service.GetDepositDueDate(new DateOnly(y, m, d)));
    }

    [Fact]
    public void GetDepositDueDate_GovernmentCollectorWithoutChallan_IsSameDay()
    {
        var date = new DateOnly(2026, 11, 18);
        Assert.Equal(date, _service.GetDepositDueDate(date, isGovernmentCollector: true, paidWithoutChallan: true));
    }

    [Fact]
    public void GetDepositDueDate_GovernmentCollectorWithChallan_IsWithinSevenDaysAfterMonthEnd()
    {
        Assert.Equal(new DateOnly(2026, 12, 7),
            _service.GetDepositDueDate(new DateOnly(2026, 11, 30), isGovernmentCollector: true));
    }

    [Fact]
    public void GetQuarterlyDeadlines_OldActUses27EqAnd27DDeadlines()
    {
        var results = _service.GetQuarterlyDeadlines(2025);
        var statement = results.Single(x => x.Code == "TCS-STATEMENT" && x.PeriodEnd == new DateOnly(2025, 6, 30));
        var certificate = results.Single(x => x.Code == "TCS-CERTIFICATE" && x.PeriodEnd == new DateOnly(2025, 6, 30));

        Assert.Equal(new DateOnly(2025, 7, 15), statement.DueDate);
        Assert.Equal("Form 27EQ", statement.Form);
        Assert.Equal(new DateOnly(2025, 7, 30), certificate.DueDate);
        Assert.Equal("Form 27D", certificate.Form);
    }

    [Fact]
    public void GetQuarterlyDeadlines_NewActUsesForm143And133Deadlines()
    {
        var results = _service.GetQuarterlyDeadlines(2026);
        var statement = results.Single(x => x.Code == "TCS-STATEMENT" && x.PeriodEnd == new DateOnly(2026, 6, 30));
        var certificate = results.Single(x => x.Code == "TCS-CERTIFICATE" && x.PeriodEnd == new DateOnly(2026, 6, 30));

        Assert.Equal(new DateOnly(2026, 7, 31), statement.DueDate);
        Assert.Equal("Form 143", statement.Form);
        Assert.Equal(new DateOnly(2026, 8, 15), certificate.DueDate);
        Assert.Equal("Form 133", certificate.Form);
        Assert.Equal(TcsComplianceAct.IncomeTaxAct2025, statement.GoverningAct);
    }

    [Fact]
    public void GetQuarterlyDeadlines_TransitionYearSeparatesOldAndNewActByQuarterEnd()
    {
        var results = _service.GetQuarterlyDeadlines(2025);
        var q4 = results.Single(x => x.Code == "TCS-STATEMENT" && x.PeriodEnd == new DateOnly(2026, 3, 31));
        Assert.Equal(TcsComplianceAct.IncomeTaxAct1961, q4.GoverningAct);
        Assert.Equal("Form 27EQ", q4.Form);
        Assert.Equal(new DateOnly(2026, 5, 15), q4.DueDate);
    }

    [Fact]
    public void GetQuarterlyDeadlines_RejectsInvalidTaxYear()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GetQuarterlyDeadlines(9999));
    }
}
