using System;
using TallyAuditAssistant.Engine.Tds.Returns;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class TdsCertificateDeadlineServiceTests
{
    private readonly TdsCertificateDeadlineService _service = new();

    [Theory]
    [InlineData(2025, 4, 1, "16", 2026, 6, 15, TdsCertificateRegime.IncomeTaxAct1961)]
    [InlineData(2026, 4, 1, "130", 2027, 6, 15, TdsCertificateRegime.IncomeTaxAct2025)]
    public void ResolveSalary_MapsFormAndAnnualDueDate(int year, int month, int day, string form,
        int dueYear, int dueMonth, int dueDay, TdsCertificateRegime regime)
    {
        var result = _service.ResolveSalary(new DateOnly(year, month, day));
        Assert.Equal(form, result.FormNumber);
        Assert.Equal(new DateOnly(dueYear, dueMonth, dueDay), result.DueDate);
        Assert.Equal(regime, result.Regime);
        Assert.Equal(TdsCertificateKind.Salary, result.Kind);
    }

    [Theory]
    [InlineData(2025, 4, 1, 2025, 6, 30, "16A", 2025, 8, 15, TdsCertificateRegime.IncomeTaxAct1961)]
    [InlineData(2025, 7, 1, 2025, 9, 30, "16A", 2025, 11, 15, TdsCertificateRegime.IncomeTaxAct1961)]
    [InlineData(2025, 10, 1, 2025, 12, 31, "16A", 2026, 2, 15, TdsCertificateRegime.IncomeTaxAct1961)]
    [InlineData(2026, 1, 1, 2026, 3, 31, "16A", 2026, 6, 15, TdsCertificateRegime.IncomeTaxAct1961)]
    [InlineData(2026, 4, 1, 2026, 6, 30, "131", 2026, 8, 15, TdsCertificateRegime.IncomeTaxAct2025)]
    [InlineData(2026, 7, 1, 2026, 9, 30, "131", 2026, 11, 15, TdsCertificateRegime.IncomeTaxAct2025)]
    [InlineData(2026, 10, 1, 2026, 12, 31, "131", 2027, 2, 15, TdsCertificateRegime.IncomeTaxAct2025)]
    [InlineData(2027, 1, 1, 2027, 3, 31, "131", 2027, 6, 15, TdsCertificateRegime.IncomeTaxAct2025)]
    public void ResolveNonSalaryQuarter_MapsTransitionAwareFormAndDeadline(
        int startYear, int startMonth, int startDay, int endYear, int endMonth, int endDay,
        string form, int dueYear, int dueMonth, int dueDay, TdsCertificateRegime regime)
    {
        var result = _service.ResolveNonSalaryQuarter(new DateOnly(startYear, startMonth, startDay),
            new DateOnly(endYear, endMonth, endDay));
        Assert.Equal(form, result.FormNumber);
        Assert.Equal(new DateOnly(dueYear, dueMonth, dueDay), result.DueDate);
        Assert.Equal(regime, result.Regime);
    }

    [Fact]
    public void ResolveNonSalaryQuarter_RejectsPartialOrInvalidQuarter()
    {
        Assert.Throws<ArgumentException>(() => _service.ResolveNonSalaryQuarter(
            new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 29)));
        Assert.Throws<ArgumentException>(() => _service.ResolveNonSalaryQuarter(
            new DateOnly(2026, 4, 2), new DateOnly(2026, 6, 30)));
    }

    [Fact]
    public void ResolveSalary_RequiresFinancialYearStart()
    {
        Assert.Throws<ArgumentException>(() => _service.ResolveSalary(new DateOnly(2026, 4, 2)));
    }

    [Fact]
    public void ResolveNonSalaryQuarter_UsesQuarterEndForGoverningAct()
    {
        var oldActQ4 = _service.ResolveNonSalaryQuarter(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));
        var newActQ1 = _service.ResolveNonSalaryQuarter(new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30));
        Assert.Equal(TdsCertificateRegime.IncomeTaxAct1961, oldActQ4.Regime);
        Assert.Equal(TdsCertificateRegime.IncomeTaxAct2025, newActQ1.Regime);
    }
}
