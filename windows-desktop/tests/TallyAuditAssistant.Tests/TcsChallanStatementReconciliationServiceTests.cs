using System;
using TallyAuditAssistant.Engine.Tds.Reconciliation;
using TallyAuditAssistant.Engine.Tds.Returns;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class TcsChallanStatementReconciliationServiceTests
{
    private readonly TcsChallanStatementReconciliationService _service = new();
    private static readonly DateOnly ChallanDate = new(2026, 7, 7);

    private static TdsChallanRecord Challan(decimal amount = 10000m, string serial = "12345") =>
        new("MUMB12345A", "2026-27", 1, "206C(1H)", "0001234", ChallanDate, serial, amount);

    private static TcsChallanAllocationLine Allocation(
        decimal amount = 10000m,
        string pan = "ABCDE1234F",
        string serial = "12345",
        string tan = "MUMB12345A") =>
        new(tan, pan, "2026-27", 1, "206C(1H)", "0001234", ChallanDate, serial, amount);

    [Fact]
    public void Reconcile_MatchesTcsChallanToQuarterlyStatementAllocation()
    {
        var result = Assert.Single(_service.Reconcile(new[] { Challan() }, new[] { Allocation() }));

        Assert.Equal("Matched", result.Status);
        Assert.Equal(10000m, result.ChallanTcsAmount);
        Assert.Equal(10000m, result.StatementAllocatedTcsAmount);
    }

    [Fact]
    public void Reconcile_FlagsTcsAmountMismatch()
    {
        var result = Assert.Single(_service.Reconcile(new[] { Challan() }, new[] { Allocation(9500m) }));

        Assert.Equal("Amount mismatch", result.Status);
        Assert.Equal(10000m, result.ChallanTcsAmount);
        Assert.Equal(9500m, result.StatementAllocatedTcsAmount);
    }

    [Fact]
    public void Reconcile_FlagsMissingAndUnallocatedChallans()
    {
        Assert.Equal("Missing challan", Assert.Single(
            _service.Reconcile(Array.Empty<TdsChallanRecord>(), new[] { Allocation() })).Status);

        Assert.Equal("Unallocated challan", Assert.Single(
            _service.Reconcile(new[] { Challan() }, Array.Empty<TcsChallanAllocationLine>())).Status);
    }

    [Fact]
    public void Reconcile_FlagsDuplicateChallansAndInvalidBuyerPan()
    {
        Assert.Equal("Duplicate challan", Assert.Single(
            _service.Reconcile(new[] { Challan(), Challan() }, new[] { Allocation() })).Status);

        Assert.Equal("Invalid deductee PAN", Assert.Single(
            _service.Reconcile(new[] { Challan() }, new[] { Allocation(pan: "BADPAN") })).Status);
    }

    [Fact]
    public void Reconcile_SeparatesDifferentTanAndFullChallanIdentificationNumber()
    {
        var result = _service.Reconcile(
            new[] { Challan() },
            new[] { Allocation(serial: "99999"), Allocation(serial: "12345", tan: "DELD54321B") });

        Assert.Equal(3, result.Count);
        Assert.Equal(2, System.Linq.Enumerable.Count(result, x => x.Status == "Missing challan"));
        Assert.Equal(1, System.Linq.Enumerable.Count(result, x => x.Status == "Unallocated challan"));
    }

    [Fact]
    public void Reconcile_RejectsInvalidQuarterAndNegativeAmounts()
    {
        Assert.Throws<ArgumentException>(() => _service.Reconcile(
            new[] { Challan() },
            new[] { Allocation() with { Quarter = 5 } }));

        Assert.Throws<ArgumentException>(() => _service.Reconcile(
            new[] { Challan(-1m) },
            Array.Empty<TcsChallanAllocationLine>()));
    }

    [Fact]
    public void Reconcile_RejectsNullInputs()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Reconcile(null!, Array.Empty<TcsChallanAllocationLine>()));
        Assert.Throws<ArgumentNullException>(() => _service.Reconcile(Array.Empty<TdsChallanRecord>(), null!));
    }
}
