using TallyAuditAssistant.Engine.Tds.Returns;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class TdsStatementWorkpaperServiceTests
{
    private readonly TdsStatementWorkpaperService _service = new();

    private static TdsStatementLine Line(DateOnly? date = null) =>
        new("ABCPE1234F", "194C", date ?? new DateOnly(2026, 4, 15), 100000m, 10000m, 9000m, "CIN-001", "V-1");

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

    [Theory]
    [InlineData(2026, 6, 30, 2026, 7, 31)]
    [InlineData(2026, 9, 30, 2026, 10, 31)]
    [InlineData(2026, 12, 31, 2027, 1, 31)]
    [InlineData(2027, 3, 31, 2027, 5, 31)]
    public void GetFilingDueDate_ReturnsQuarterlyStatutoryDeadline(int y, int m, int d, int dueY, int dueM, int dueD)
    {
        var result = _service.GetFilingDueDate(new DateOnly(y, m, d));
        Assert.Equal(new DateOnly(dueY, dueM, dueD), result);
    }

    [Fact]
    public void GetFilingDueDate_RejectsNonQuarterEndDate()
    {
        Assert.Throws<ArgumentException>(() => _service.GetFilingDueDate(new DateOnly(2026, 6, 29)));
    }

    [Fact]
    public void Prepare_BuildsQuarterlyWorkpaperAndControlTotals()
    {
        var lines = new[] { Line() with { DeducteeReference = "V-1" }, Line(new(2026, 6, 30)) with { DeducteeReference = "V-2" } };
        var challans = new[]
        {
            new TdsStatementChallan("CIN-001", new(2026, 5, 7), 18000m, Surcharge: 0, Cess: 0, Interest: 100m, Fee: 0)
        };

        var result = _service.Prepare(TdsStatementKind.NonSalaryResident,
            new(2026, 4, 1), new(2026, 6, 30), "TY 2026-27", 1, "ABCD12345E", lines, challans);

        Assert.Equal("140", result.FormNumber);
        Assert.Equal(new DateOnly(2026, 7, 31), result.FilingDueDate);
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
    public void Prepare_ExcludesAllRowsWithDuplicateDeducteeSourceReferences()
    {
        var duplicateOne = new TdsStatementLine("ABCPE1234F", "194C", new(2026, 4, 15),
            100000m, 10000m, 10000m, "CIN-1", "SOURCE-42");
        var duplicateTwo = duplicateOne with { AmountPaidOrCredited = 125000m, TaxDeducted = 12500m };
        var independent = duplicateOne with { DeducteeReference = "SOURCE-43" };

        var result = _service.Prepare(TdsStatementKind.NonSalaryResident,
            new(2026, 4, 1), new(2026, 6, 30), "TY 2026-27", 1,
            "ABCD12345E", new[] { duplicateOne, duplicateTwo, independent });

        var issue = Assert.Single(result.Issues.Where(x => x.Code == "DUPLICATE_DEDUCTEE_REFERENCE"));
        Assert.Equal("SOURCE-42", issue.Reference);
        Assert.Single(result.Lines);
        Assert.Equal("SOURCE-43", result.Lines[0].DeducteeReference);
        Assert.Equal(100000m, result.TotalAmountPaidOrCredited);
        Assert.Equal(1, result.AcceptedLineCount);
        Assert.False(result.IsReadyForReview);
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
    public void Prepare_ValidatesDeducteeChallanAllocationsAndOverbooking()
    {
        var lines = new[]
        {
            new TdsStatementLine("ABCPE1234F", "194C", new(2026, 4, 15), 100000m, 10000m, 7000m, "CIN-1", "V-1"),
            new TdsStatementLine("ABCPE1234F", "194C", new(2026, 4, 16), 100000m, 10000m, 4000m, "CIN-1", "V-2"),
            new TdsStatementLine("ABCPE1234F", "194C", new(2026, 4, 17), 100000m, 10000m, 1000m, "UNKNOWN", "V-3"),
            new TdsStatementLine("ABCPE1234F", "194C", new(2026, 4, 18), 100000m, 10000m, 500m, null, "V-4")
        };
        var challans = new[] { new TdsStatementChallan("CIN-1", new(2026, 5, 7), 10000m) };

        var result = _service.Prepare(TdsStatementKind.NonSalaryResident,
            new(2026, 4, 1), new(2026, 6, 30), "TY 2026-27", 1, "ABCD12345E", lines, challans);

        Assert.Contains(result.Issues, x => x.Code == "OVERBOOKED_CHALLAN_ALLOCATION");
        Assert.Contains(result.Issues, x => x.Code == "UNMATCHED_LINE_CHALLAN_REFERENCE" && x.Reference == "UNKNOWN");
        Assert.Contains(result.Issues, x => x.Code == "MISSING_LINE_CHALLAN_REFERENCE");
        Assert.False(result.IsReadyForReview);
    }

    [Theory]
    [InlineData("ABCDE1234F")]
    [InlineData("ABCDX1234F")]
    [InlineData("ABCPE1234")]
    public void Prepare_RejectsPanWithInvalidEntityCodeOrStructure(string pan)
    {
        var result = _service.Prepare(TdsStatementKind.NonSalaryResident,
            new(2026, 4, 1), new(2026, 6, 30), "TY 2026-27", 1,
            "ABCD12345E", new[] { Line() with { DeducteePan = pan } });

        Assert.Empty(result.Lines);
        Assert.Contains(result.Issues, x => x.Code == "INVALID_DEDUCTEE_PAN");
    }

    [Fact]
    public void Prepare_RejectsChallanWithoutDepositDate()
    {
        var result = _service.Prepare(TdsStatementKind.NonSalaryResident,
            new(2026, 4, 1), new(2026, 6, 30), "TY 2026-27", 1,
            "ABCD12345E", new[] { Line() },
            new[] { new TdsStatementChallan("CIN-001", default, 9000m) });

        Assert.Contains(result.Issues, x => x.Code == "INVALID_CHALLAN_DATE");
        Assert.Empty(result.Challans);
        Assert.False(result.IsReadyForReview);
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
