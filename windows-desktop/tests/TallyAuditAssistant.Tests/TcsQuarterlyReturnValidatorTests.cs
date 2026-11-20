using System;
using System.Collections.Generic;
using System.Linq;
using TallyAuditAssistant.Engine.Tds.Returns;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class TcsQuarterlyReturnValidatorTests
{
    private readonly TcsQuarterlyReturnValidator _validator = new();

    [Fact]
    public void Validate_OldActQuarter_RequiresForm27Eq()
    {
        var statement = Statement("Form 27EQ", 2025, 1, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30));
        Assert.DoesNotContain(_validator.Validate(statement), x => x.Severity == "Error");
    }

    [Fact]
    public void Validate_NewActQuarter_RequiresForm143()
    {
        var statement = Statement("Form 143", 2026, 1, new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30));
        Assert.Empty(_validator.Validate(statement));
    }

    [Fact]
    public void Validate_TransitionYearQ4_RemainsUnderOldAct()
    {
        var statement = Statement("Form 27EQ", 2025, 4, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));
        Assert.DoesNotContain(_validator.Validate(statement), x => x.Code == "FORM_ACT_MISMATCH");
    }

    [Fact]
    public void Validate_FlagsWrongFormAndQuarterDates()
    {
        var statement = Statement("Form 143", 2025, 1, new DateOnly(2025, 4, 2), new DateOnly(2025, 6, 30));
        var findings = _validator.Validate(statement);
        Assert.Contains(findings, x => x.Code == "FORM_ACT_MISMATCH");
        Assert.Contains(findings, x => x.Code == "PERIOD_MISMATCH");
    }

    [Fact]
    public void Validate_FlagsInvalidPanDuplicateReferenceAndOutOfPeriodCollection()
    {
        var valid = Line("TX-1", "BADPAN", new DateOnly(2025, 7, 1));
        var second = Line("TX-1", "ABCDE1234F", new DateOnly(2025, 6, 30));
        var statement = Statement("Form 27EQ", 2025, 1,
            new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), valid, second);
        var findings = _validator.Validate(statement);

        Assert.Contains(findings, x => x.Code == "INVALID_BUYER_PAN");
        Assert.Contains(findings, x => x.Code == "DUPLICATE_TRANSACTION_REFERENCE");
        Assert.Contains(findings, x => x.Code == "COLLECTION_OUTSIDE_PERIOD");
    }

    [Fact]
    public void Validate_FlagsLateFilingAfterStandardDueDate()
    {
        var statement = Statement("Form 27EQ", 2025, 1,
            new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), filedOn: new DateOnly(2025, 7, 16));
        Assert.Contains(_validator.Validate(statement), x => x.Code == "LATE_FILING");
    }

    [Fact]
    public void Validate_RejectsInvalidQuarter()
    {
        var statement = new TcsQuarterlyReturn("ABCD12345E", "Form 27EQ", 2025, 5,
            new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), null,
            Array.Empty<TcsQuarterlyReturnLine>());
        Assert.Contains(_validator.Validate(statement), x => x.Code == "INVALID_PERIOD");
    }

    private static TcsQuarterlyReturn Statement(string form, int fy, int quarter, DateOnly start, DateOnly end,
        params TcsQuarterlyReturnLine[] lines)
        => Statement(form, fy, quarter, start, end, null, lines);

    private static TcsQuarterlyReturn Statement(string form, int fy, int quarter, DateOnly start, DateOnly end,
        DateOnly? filedOn, params TcsQuarterlyReturnLine[] lines)
        => new("ABCD12345E", form, fy, quarter, start, end, filedOn, lines);

    private static TcsQuarterlyReturnLine Line(string reference, string pan, DateOnly date)
        => new(reference, pan, "206C(1H)", date, 100000m, 1000m);
}
