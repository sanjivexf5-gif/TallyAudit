using System;
using TallyAuditAssistant.Engine.Tds.Reconciliation;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class TdsChallanReturnReconciliationServiceTests
{
    private readonly TdsChallanReturnReconciliationService _service = new();
    private static readonly DateOnly ChallanDate = new(2026, 7, 7);

    private static TdsChallanRecord Challan(decimal amount = 10000m, string serial = "12345") =>
        new("MUMB12345A", "2026-27", 1, "194C", "0001234", ChallanDate, serial, amount);

    private static TdsReturnLine Line(decimal amount = 10000m, string pan = "ABCDE1234F", string serial = "12345") =>
        new(pan, "2026-27", 1, "194C", "0001234", ChallanDate, serial, amount, "Test Deductee");

    [Fact]
    public void Reconcile_MatchesChallanAndReturnAllocations()
    {
        var result = Assert.Single(_service.Reconcile(new[] { Challan() }, new[] { Line() }));
        Assert.Equal("Matched", result.Status);
        Assert.Equal(10000m, result.StatementAllocatedAmount);
    }

    [Fact]
    public void Reconcile_FlagsAmountMismatch()
    {
        var result = Assert.Single(_service.Reconcile(new[] { Challan(10000m) }, new[] { Line(9500m) }));
        Assert.Equal("Amount mismatch", result.Status);
    }

    [Fact]
    public void Reconcile_FlagsMissingAndUnallocatedChallans()
    {
        Assert.Equal("Missing challan", Assert.Single(_service.Reconcile(Array.Empty<TdsChallanRecord>(), new[] { Line() })).Status);
        Assert.Equal("Unallocated challan", Assert.Single(_service.Reconcile(new[] { Challan() }, Array.Empty<TdsReturnLine>())).Status);
    }

    [Fact]
    public void Reconcile_FlagsDuplicateChallanAndInvalidPan()
    {
        Assert.Equal("Duplicate challan", Assert.Single(_service.Reconcile(new[] { Challan(), Challan() }, new[] { Line() })).Status);
        Assert.Equal("Invalid deductee PAN", Assert.Single(_service.Reconcile(new[] { Challan() }, new[] { Line(pan: "BADPAN") })).Status);
    }

    [Fact]
    public void Reconcile_UsesFullCinAndPeriodKey()
    {
        var another = Challan(10000m, "99999");
        var result = _service.Reconcile(new[] { Challan() }, new[] { Line(serial: "99999") });
        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.Status == "Missing challan");
        Assert.Contains(result, x => x.Status == "Unallocated challan");
    }

    [Fact]
    public void Reconcile_RejectsNullAndInvalidQuarter()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Reconcile(null!, Array.Empty<TdsReturnLine>()));
        var invalid = Line() with { Quarter = 5 };
        Assert.Throws<ArgumentException>(() => _service.Reconcile(new[] { Challan() }, new[] { invalid }));
    }
}
