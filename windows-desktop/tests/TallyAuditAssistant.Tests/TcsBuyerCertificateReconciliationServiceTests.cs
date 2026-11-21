using System;
using TallyAuditAssistant.Engine.Tds.Returns;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class TcsBuyerCertificateReconciliationServiceTests
{
    private readonly TcsBuyerCertificateReconciliationService _service = new();
    private static TcsQuarterlyReturn Statement(int fy = 2025, int quarter = 1, params TcsQuarterlyReturnLine[] lines)
    {
        var start = quarter switch { 1 => new DateOnly(fy, 4, 1), 2 => new DateOnly(fy, 7, 1),
            3 => new DateOnly(fy, 10, 1), _ => new DateOnly(fy + 1, 1, 1) };
        return new TcsQuarterlyReturn("ABCD12345E", fy < 2026 ? "Form 27EQ" : "Form 143",
            fy, quarter, start, start.AddMonths(3).AddDays(-1), null, lines);
    }
    private static TcsQuarterlyReturnLine Line(string pan = "ABCDE1234F", decimal sale = 100000m, decimal tcs = 1000m,
        DateOnly? date = null) => new("TX-1", pan, "206C(1H)", date ?? new DateOnly(2025, 4, 15), sale, tcs);
    private static TcsBuyerCertificate Certificate(string pan = "ABCDE1234F", string number = "CERT-001",
        string form = "Form 27D", DateOnly? issued = null, decimal sale = 100000m, decimal tcs = 1000m)
        => new(number, pan, form, issued ?? new DateOnly(2025, 7, 30), sale, tcs);

    [Fact]
    public void Reconcile_MatchesBuyerWiseCertificateAmounts()
    {
        var result = Assert.Single(_service.Reconcile(Statement(2025, 1, Line()), new[] { Certificate() }));
        Assert.Equal("MATCHED", result.Code);
        Assert.Equal("Info", result.Severity);
    }
    [Fact]
    public void Reconcile_FlagsMissingCertificate()
    {
        var result = Assert.Single(_service.Reconcile(Statement(2025, 1, Line()), Array.Empty<TcsBuyerCertificate>()));
        Assert.Equal("MISSING_CERTIFICATE", result.Code);
    }
    [Fact]
    public void Reconcile_FlagsBuyerWiseAmountMismatch()
    {
        var result = Assert.Single(_service.Reconcile(Statement(2025, 1, Line()), new[] { Certificate(sale: 99000m) }));
        Assert.Equal("CERTIFICATE_AMOUNT_MISMATCH", result.Code);
    }
    [Fact]
    public void Reconcile_UsesNewActFormForNewActQuarter()
    {
        var line = Line(date: new DateOnly(2026, 4, 15), sale: 50000m, tcs: 500m);
        var result = Assert.Single(_service.Reconcile(Statement(2026, 1, line),
            new[] { Certificate(form: "Form 133", issued: new DateOnly(2026, 7, 20), sale: 50000m, tcs: 500m) }));
        Assert.Equal("MATCHED", result.Code);
    }
    [Fact]
    public void Reconcile_UsesOldActForTransitionYearFourthQuarter()
    {
        var line = Line(date: new DateOnly(2026, 2, 15), sale: 50000m, tcs: 500m);
        var result = Assert.Single(_service.Reconcile(Statement(2025, 4, line),
            new[] { Certificate(form: "Form 27D", issued: new DateOnly(2026, 5, 25), sale: 50000m, tcs: 500m) }));
        Assert.Equal("MATCHED", result.Code);
    }
    [Fact]
    public void Reconcile_FlagsLateCertificateAndWrongForm()
    {
        var result = _service.Reconcile(Statement(2025, 1, Line()),
            new[] { Certificate(form: "Form 133", issued: new DateOnly(2025, 8, 1)) });
        Assert.Contains(result, x => x.Code == "CERTIFICATE_FORM_MISMATCH");
        Assert.Contains(result, x => x.Code == "LATE_CERTIFICATE");
    }
    [Fact]
    public void Reconcile_FlagsMultipleCertificatesAndDuplicateNumberForSameBuyer()
    {
        var result = _service.Reconcile(Statement(2025, 1, Line()),
            new[] { Certificate(), Certificate(number: "CERT-001") });
        Assert.Contains(result, x => x.Code == "MULTIPLE_BUYER_CERTIFICATES");
        Assert.Equal(2, System.Linq.Enumerable.Count(result, x => x.Code == "DUPLICATE_CERTIFICATE_NUMBER"));
    }
    [Fact]
    public void Reconcile_FlagsDuplicateCertificateNumberAcrossDifferentBuyers()
    {
        var secondPan = "FGHIJ5678K";
        var statement = Statement(2025, 1, Line(), Line(secondPan));
        var certificates = new[] { Certificate(), Certificate(pan: secondPan) };
        var result = _service.Reconcile(statement, certificates);
        Assert.Equal(2, System.Linq.Enumerable.Count(result, x => x.Code == "DUPLICATE_CERTIFICATE_NUMBER"));
        Assert.DoesNotContain(result, x => x.Code == "MATCHED");
    }
    [Fact]
    public void Reconcile_FlagsMissingOrPreQuarterCertificateIssueDate()
    {
        var missing = _service.Reconcile(Statement(2025, 1, Line()),
            new[] { Certificate(issued: DateOnly.MinValue) });
        Assert.Contains(missing, x => x.Code == "INVALID_CERTIFICATE_ISSUE_DATE");
        var early = _service.Reconcile(Statement(2025, 1, Line()),
            new[] { Certificate(issued: new DateOnly(2025, 6, 29)) });
        Assert.Contains(early, x => x.Code == "INVALID_CERTIFICATE_ISSUE_DATE");
    }
    [Fact]
    public void Reconcile_RejectsInvalidStatementQuarterAndNullInputs()
    {
        var invalid = new TcsQuarterlyReturn("ABCD12345E", "Form 27EQ", 2025, 5,
            new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30), null, Array.Empty<TcsQuarterlyReturnLine>());
        Assert.Throws<ArgumentException>(() => _service.Reconcile(invalid, Array.Empty<TcsBuyerCertificate>()));
        Assert.Throws<ArgumentNullException>(() => _service.Reconcile(null!, Array.Empty<TcsBuyerCertificate>()));
        Assert.Throws<ArgumentNullException>(() => _service.Reconcile(Statement(), null!));
    }
}