using TallyAuditAssistant.Engine.Tds.Returns;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class TdsDeducteeCertificateReconciliationServiceTests
{
    private readonly TdsDeducteeCertificateReconciliationService _service = new();

    private static TdsStatementWorkpaper Statement(
        DateOnly? start = null, DateOnly? end = null,
        TdsStatementKind kind = TdsStatementKind.NonSalaryResident,
        params TdsStatementLine[] lines)
    {
        var periodStart = start ?? new DateOnly(2026, 4, 1);
        var periodEnd = end ?? new DateOnly(2026, 6, 30);
        return new TdsStatementWorkpaper
        {
            StatementKind = kind,
            Regime = TdsLegislativeRegime.IncomeTaxAct2025,
            FormNumber = "140",
            Period = new TdsStatementPeriod(periodStart, periodEnd, 1, "TY 2026-27"),
            Lines = lines
        };
    }

    private static TdsStatementLine Line(string pan = "ABCDE1234F",
        decimal amount = 100000m, decimal tax = 10000m) =>
        new(pan, "194C", new DateOnly(2026, 4, 15), amount, tax, tax, "CIN-1", "V-1");

    private static TdsDeducteeCertificate Certificate(string pan = "ABCDE1234F",
        string number = "CERT-001", string form = "131",
        DateOnly? start = null, DateOnly? end = null, DateOnly? issued = null,
        decimal amount = 100000m, decimal tax = 10000m) =>
        new(number, pan, form, start ?? new DateOnly(2026, 4, 1),
            end ?? new DateOnly(2026, 6, 30), issued ?? new DateOnly(2026, 8, 10), amount, tax);

    [Fact]
    public void Reconcile_ReturnsMatchedWhenAmountsAndPeriodAgree()
    {
        var result = _service.Reconcile(Statement(null, null, TdsStatementKind.NonSalaryResident, Line()),
            new[] { Certificate() });

        var finding = Assert.Single(result);
        Assert.Equal("MATCHED", finding.Code);
        Assert.Equal("Info", finding.Severity);
    }

    [Fact]
    public void Reconcile_UsesOldActFormForQuarterEndingMarch2026()
    {
        var statement = Statement(new(2026, 1, 1), new(2026, 3, 31),
            TdsStatementKind.NonSalaryResident,
            new TdsStatementLine("ABCDE1234F", "194C", new(2026, 2, 15), 100000m, 10000m, 10000m));
        var certificate = Certificate(form: "16A", start: new(2026, 1, 1),
            end: new(2026, 3, 31), issued: new(2026, 6, 10));

        var result = _service.Reconcile(statement, new[] { certificate });

        Assert.Contains(result, x => x.Code == "MATCHED");
        Assert.DoesNotContain(result, x => x.Code == "CERTIFICATE_FORM_MISMATCH");
    }

    [Fact]
    public void Reconcile_ReportsMissingCertificateAndExtraCertificate()
    {
        var statement = Statement(null, null, TdsStatementKind.NonSalaryResident, Line());
        var result = _service.Reconcile(statement, new[] { Certificate("FGHIJ5678K", number: "CERT-2") });

        Assert.Contains(result, x => x.Code == "MISSING_CERTIFICATE" && x.DeducteePan == "ABCDE1234F");
        Assert.Contains(result, x => x.Code == "CERTIFICATE_WITHOUT_STATEMENT" && x.DeducteePan == "FGHIJ5678K");
    }

    [Fact]
    public void Reconcile_ReportsFormPeriodAndAmountMismatches()
    {
        var wrong = Certificate(form: "16A", start: new(2026, 7, 1), end: new(2026, 9, 30),
            amount: 90000m, tax: 9000m);
        var result = _service.Reconcile(Statement(null, null, TdsStatementKind.NonSalaryResident, Line()),
            new[] { wrong });

        Assert.Contains(result, x => x.Code == "CERTIFICATE_FORM_MISMATCH");
        Assert.Contains(result, x => x.Code == "CERTIFICATE_PERIOD_MISMATCH");
        Assert.Contains(result, x => x.Code == "CERTIFICATE_AMOUNT_MISMATCH");
    }

    [Fact]
    public void Reconcile_ReportsLateIssueDateButHonoursMatchedAmounts()
    {
        var late = Certificate(issued: new(2026, 8, 20));
        var result = _service.Reconcile(Statement(null, null, TdsStatementKind.NonSalaryResident, Line()),
            new[] { late });

        Assert.Contains(result, x => x.Code == "LATE_CERTIFICATE" && x.Severity == "Warning");
        Assert.DoesNotContain(result, x => x.Code == "MATCHED");
    }

    [Fact]
    public void Reconcile_ReportsDuplicateCertificateNumbers()
    {
        var duplicate = new[] { Certificate(), Certificate(number: "CERT-001") };
        var result = _service.Reconcile(Statement(null, null, TdsStatementKind.NonSalaryResident, Line()), duplicate);

        Assert.Contains(result, x => x.Code == "DUPLICATE_CERTIFICATE_NUMBER");
        Assert.Contains(result, x => x.Code == "MULTIPLE_DEDUCTEE_CERTIFICATES");
    }

    [Fact]
    public void Reconcile_RejectsSalaryAndTcsWorkpapers()
    {
        var salary = Statement(null, null, TdsStatementKind.Salary, Line());
        var tcs = Statement(null, null, TdsStatementKind.TaxCollectedAtSource, Line());

        Assert.Throws<ArgumentException>(() => _service.Reconcile(salary, Array.Empty<TdsDeducteeCertificate>()));
        Assert.Throws<ArgumentException>(() => _service.Reconcile(tcs, Array.Empty<TdsDeducteeCertificate>()));
    }

    [Fact]
    public void Reconcile_RejectsInvalidQuarterPeriod()
    {
        var invalid = Statement(new(2026, 4, 1), new(2026, 6, 29),
            TdsStatementKind.NonSalaryResident, Line());
        Assert.Throws<ArgumentException>(() => _service.Reconcile(invalid, Array.Empty<TdsDeducteeCertificate>()));
    }

    [Fact]
    public void Reconcile_RejectsNullInputs()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Reconcile(null!, Array.Empty<TdsDeducteeCertificate>()));
        Assert.Throws<ArgumentNullException>(() => _service.Reconcile(Statement(), null!));
    }
}
