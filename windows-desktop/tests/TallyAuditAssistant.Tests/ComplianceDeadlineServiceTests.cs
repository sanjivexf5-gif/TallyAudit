using System;
using System.Linq;
using TallyAuditAssistant.Engine.Compliance;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class ComplianceDeadlineServiceTests
{
    private readonly ComplianceDeadlineService _service = new();

    [Fact]
    public void GenerateUpcoming_ReturnsMonthlyGstAndTdsRemindersInDueDateOrder()
    {
        var results = _service.GenerateUpcoming(new DateTime(2026, 11, 1), monthsAhead: 1);

        Assert.Contains(results, x => x.Code == "GST-GSTR1-M" && x.TaxPeriod == "Oct 2026" && x.DueDate == new DateOnly(2026, 11, 11));
        Assert.Contains(results, x => x.Code == "GST-GSTR3B-M" && x.TaxPeriod == "Oct 2026" && x.DueDate == new DateOnly(2026, 11, 20));
        Assert.Contains(results, x => x.Code == "TDS-DEPOSIT-M" && x.TaxPeriod == "Oct 2026" && x.DueDate == new DateOnly(2026, 11, 7));
        Assert.Equal(results.OrderBy(x => x.DueDate), results);
    }

    [Fact]
    public void GenerateUpcoming_UsesMarchTdsSpecialDueDate()
    {
        var result = _service.GenerateUpcoming(new DateTime(2026, 4, 1), monthsAhead: 1)
            .Single(x => x.Code == "TDS-DEPOSIT-M" && x.TaxPeriod == "Mar 2026");

        Assert.Equal(new DateOnly(2026, 4, 30), result.DueDate);
    }

    [Fact]
    public void GenerateUpcoming_ClassifiesOverdueTodayAndUpcoming()
    {
        var rule = new ComplianceDeadlineRule("TEST", "Test", "GST", 10, 0, "Test", "local");
        var results = _service.GenerateUpcoming(new DateTime(2026, 11, 10), 0, new[] { rule });

        Assert.Contains(results, x => x.DueDate == new DateOnly(2026, 10, 10) && x.Status == "Overdue");
        Assert.Contains(results, x => x.DueDate == new DateOnly(2026, 11, 10) && x.Status == "Due today");
    }

    [Fact]
    public void GenerateUpcoming_ClampsMonthEndForShortMonths()
    {
        var rule = new ComplianceDeadlineRule("MONTH-END", "Month end", "GST", 31, 1, "Test", "local");
        var result = _service.GenerateUpcoming(new DateTime(2026, 1, 1), 2, new[] { rule })
            .Single(x => x.TaxPeriod == "Jan 2026");

        Assert.Equal(new DateOnly(2026, 2, 28), result.DueDate);
    }

    [Fact]
    public void GenerateUpcoming_RejectsInvalidHorizonAndRules()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GenerateUpcoming(DateTime.Today, 25));
        var invalid = new ComplianceDeadlineRule("BAD", "Bad", "GST", 0, 1, "Test", "local");
        Assert.Throws<ArgumentException>(() => _service.GenerateUpcoming(DateTime.Today, 1, new[] { invalid }));
    }
}
