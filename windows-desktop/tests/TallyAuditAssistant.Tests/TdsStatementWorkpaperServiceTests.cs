using TallyAuditAssistant.Engine.Tds.Returns;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class TdsStatementWorkpaperServiceTests
{
    private readonly TdsStatementWorkpaperService _service = new();

    private static TdsStatementLine Line(DateOnly? date = null) =>
        new("ABCDE1234F", "194C", date ?? new DateOnly(2026, 4, 15), 100000m, 10000m, 9000m, "CIN-001", "V-1");

    [Theory]
    [InlineData(TdsStatementKind.Salary, "24Q")]
    [InlineData(TdsStatementKind.NonSalaryResident, "26Q")]
    [InlineData(TdsStatementKind.NonSalaryNonResident, "27Q")]
    [InlineData(TdsStatementKind.TaxCollectedAtSource, "27EQ")]
    public void ResolveForm_UsesOldActFormsThroughMarch2026(TdsStatementKind kind, string expected)
    {
        var result = _service.ResolveForm(kind, new(2026, 1, 1), new(2026, 3, 31));
        Assert.Equal(TdsLegislativeRegime.IncomeTaxAct1961, result.Regime);
        Assert.Equal(expected, result.FormNumber);
    }

    [Theory]
    [InlineData(TdsStatementKind.Salary, "138")]
    [InlineData(TdsStatementKind.NonSalaryResident, "140")]
    [InlineData(TdsStatementKind.NonSalaryNonResident, "144")]
    [InlineData(TdsStatementKind.TaxCollectedAtSource, "143")]
    public void ResolveForm_UsesNewActFormsFromApril2026(TdsStatementKind kind, string expected)
    {
        var result = _service.ResolveForm(kind, new(2026, 4, 1), new(2026, 6, 30), quarter: 1);
        Assert.Equal(TdsLegislativeRegime.IncomeTaxAct2025, result.Regime);
        Assert.Equal(expected, result.FormNumber);
        Assert.Equal("1.2", result.DataStructureVersion is null ? null : result.DataStructureVersion[..3]);
    }

    [Fact]
    public void ResolveForm_UsesQuarterSpecificSalaryUtilityVersion()
    {
        var q1 = _service.ResolveForm(TdsStatementKind.Salary, new(2026, 4, 1), new(2026, 6, 30), 1);
        var q4 = _service.ResolveForm(TdsStatementKind.Salary, new(2027, 1, 1), new(2027, 3, 31), 4);
        Assert.Equal("1.2 (Q1-Q3)", q1.DataStructureVersion);
        Assert.Equal("1.0 (Q4; verify current release)", q4.DataStructureVersion);
    }

    [Fact]
    public void ResolveForm_RejectsPeriodCrossingLegislativeTransition()
    {
        Assert.Throws<ArgumentException>(() => _service.ResolveForm(TdsStatementKind.NonSalaryResident,
            new(2026, 1, 1), new(2026, 6, 30)));
    }

    [Fact]
    public void Prepare_BuildsQuarterlyWorkpaperAndControlTotals()
    {
        var lines = new[] { Line(), Line(new(2026, 6, 30)) };
        var challans = new[]
        {
            new TdsStatementChallan("CIN-001", new(2026, 5, 7), 18000m, Surcharge: 0, Cess: 0, Interest: 100m, Fee: 0)
        };

        var result = _service.Prepare(TdsStatementKind.NonSalaryResident,
            new(2026, 4, 1), new(2026, 6, 30), "TY 2026-27", 1, "ABCD12345E", lines, challans);

        Assert.Equal("140", result.FormNumber);
        Assert.Equal(TdsLegislativeRegime.IncomeTaxAct2025, result.Regime);
        Assert.Equal(200000m, result.TotalAmountPaidOrCredited);
        Assert.Equal(20000m, result.TotalTaxDeducted);
        Assert.Equal(18000m, result.TotalTaxDepositedOnLines);
        Assert.Equal(18100m, result.NetTaxAndChargesDeposited);
        Assert.True(result.IsReadyForReview);
    }

    [Fact]
    public void Prepare_ReportsLineLevelValidationAndExcludesInvalidLines()
    {
        var invalid = new TdsStatementLine("BADPAN", "", new(2026, 7, 1), -10m, 20m, 30m);
        var result = _service.Prepare(TdsStatementKind.NonSalaryResident,
            new(2026, 4, 1), new(2026, 6, 30), "TY 2026-27", 1, "BADTAN", new[] { invalid });

        Assert.Empty(result.Lines);
        Assert.False(result.IsReadyForReview);
        Assert.Contains(result.Issues, x => x.Code == "INVALID_DEDUCTOR_TAN");
        Assert.Contains(result.Issues, x => x.Code == "INVALID_DEDUCTEE_PAN");
        Assert.Contains(result.Issues, x => x.Code == "DATE_OUTSIDE_PERIOD");
        Assert.Contains(result.Issues, x => x.Code == "NEGATIVE_PAYMENT_AMOUNT");
        Assert.Contains(result.Issues, x => x.Code == "TDS_EXCEEDS_PAYMENT");
        Assert.Contains(result.Issues, x => x.Code == "DEPOSIT_EXCEEDS_DEDUCTION");
    }

    [Fact]
    public void Prepare_ValidatesQuarterDatesAndRejectsQuarterCrossing()
    {
        Assert.Throws<ArgumentException>(() => _service.Prepare(TdsStatementKind.Salary,
            new(2026, 4, 1), new(2026, 7, 1), "TY 2026-27", 1, "ABCD12345E", Array.Empty<TdsStatementLine>()));
        Assert.Throws<ArgumentException>(() => _service.Prepare(TdsStatementKind.Salary,
            new(2026, 1, 1), new(2026, 6, 30), "FY 2025-26", 4, "ABCD12345E", Array.Empty<TdsStatementLine>()));
    }

    [Fact]
    public void Prepare_RejectsDuplicateChallanReferencesAndNegativeComponents()
    {
        var challans = new[]
        {
            new TdsStatementChallan("CIN-1", new(2026, 5, 7), 100m),
            new TdsStatementChallan("CIN-1", new(2026, 5, 7), 100m),
            new TdsStatementChallan("CIN-2", new(2026, 5, 7), -1m)
        };
        var result = _service.Prepare(TdsStatementKind.Salary, new(2026, 4, 1), new(2026, 6, 30),
            "TY 2026-27", 1, "ABCD12345E", new[] { Line() }, challans);
        Assert.Contains(result.Issues, x => x.Code == "DUPLICATE_CHALLAN_REFERENCE");
        Assert.Contains(result.Issues, x => x.Code == "NEGATIVE_CHALLAN_COMPONENT");
    }

    [Fact]
    public void Prepare_ValidatesOldActQuarterFourPeriod()
    {
        var result = _service.Prepare(TdsStatementKind.Salary, new(2026, 1, 1), new(2026, 3, 31),
            "FY 2025-26", 4, "ABCD12345E", Array.Empty<TdsStatementLine>());
        Assert.Equal("24Q", result.FormNumber);
        Assert.Equal(TdsLegislativeRegime.IncomeTaxAct1961, result.Regime);
    }
}
