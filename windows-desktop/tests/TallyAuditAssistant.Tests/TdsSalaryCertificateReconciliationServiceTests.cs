using TallyAuditAssistant.Engine.Tds.Returns;
using Xunit;

namespace TallyAuditAssistant.Tests;

public sealed class TdsSalaryCertificateReconciliationServiceTests
{
    private readonly TdsSalaryCertificateReconciliationService _service = new();

    private static TdsStatementWorkpaper[] Statements(
        DateOnly? yearStart = null, int? missingQuarter = null,
        TdsStatementKind kind = TdsStatementKind.Salary)
    {
        var start = yearStart ?? new DateOnly(2026, 4, 1);
        return Enumerable.Range(1, 4).Where(q => q != missingQuarter).Select(q =>
        {
            var quarterStart = start.AddMonths((q - 1) * 3);
            var quarterEnd = quarterStart.AddMonths(3).AddDays(-1);
            var pan = "ABCDE1234F";
            var line = new TdsStatementLine(pan, "192", quarterStart.AddDays(10),
                250000m, 25000m, 25000m, $"CIN-{q}", $"SAL-{q}");
            var oldAct = quarterEnd < new DateOnly(2026, 4, 1);
            return new TdsStatementWorkpaper
            {
                StatementKind = kind,
                Regime = oldAct ? TdsLegislativeRegime.IncomeTaxAct1961 : TdsLegislativeRegime.IncomeTaxAct2025,
                FormNumber = oldAct ? "24Q" : "138",
                Period = new TdsStatementPeriod(quarterStart, quarterEnd, q,
                    oldAct ? "FY 2025-26" : "TY 2026-27"),
                Lines = new[] { line }
            };
        }).ToArray();
    }

    private static TdsDeducteeCertificate Certificate(
        DateOnly? start = null, DateOnly? end = null, string form = "130",
        DateOnly? issued = null, decimal amount = 1000000m, decimal tax = 100000m,
        string pan = "ABCDE1234F", string number = "FORM16-001") =>
        new(number, pan, form, start ?? new DateOnly(2026, 4, 1),
            end ?? new DateOnly(2027, 3, 31), issued ?? new DateOnly(2027, 6, 10),
            amount, tax);

    [Fact]
    public void Reconcile_MatchesNewActAnnualSalaryCertificate()
    {
        var result = _service.Reconcile(Statements(), new[] { Certificate() });
        var finding = Assert.Single(result);
        Assert.Equal("MATCHED", finding.Code);
        Assert.Equal("Info", finding.Severity);
    }

    [Fact]
    public void Reconcile_UsesOldActForm16ForFinancialYearEndingMarch2026()
    {
        var result = _service.Reconcile(
            Statements(new DateOnly(2025, 4, 1)),
            new[] { Certificate(new DateOnly(2025, 4, 1), new DateOnly(2026, 3, 31),
                "16", new DateOnly(2026, 6, 10), 1000000m, 100000m) });

        Assert.Contains(result, x => x.Code == "MATCHED");
        Assert.DoesNotContain(result, x => x.Code == "CERTIFICATE_FORM_MISMATCH");
    }

    [Fact]
    public void Reconcile_ReportsMissingCertificateAndCertificateWithoutStatement()
    {
        var extra = Certificate(pan: "FGHIJ5678K", number: "FORM16-002");
        var result = _service.Reconcile(Statements(), new[] { extra });
        Assert.Contains(result, x => x.Code == "MISSING_CERTIFICATE" && x.DeducteePan == "ABCDE1234F");
        Assert.Contains(result, x => x.Code == "CERTIFICATE_WITHOUT_STATEMENT" && x.DeducteePan == "FGHIJ5678K");
    }

    [Fact]
    public void Reconcile_ReportsFormPeriodAndAnnualAmountMismatches()
    {
        var wrong = Certificate(start: new DateOnly(2026, 7, 1),
            end: new DateOnly(2027, 6, 30), form: "16",
            amount: 900000m, tax: 90000m);
        var result = _service.Reconcile(Statements(), new[] { wrong });
        Assert.Contains(result, x => x.Code == "CERTIFICATE_FORM_MISMATCH");
        Assert.Contains(result, x => x.Code == "CERTIFICATE_PERIOD_MISMATCH");
        Assert.Contains(result, x => x.Code == "CERTIFICATE_AMOUNT_MISMATCH");
    }

    [Fact]
    public void Reconcile_WarnsWhenAnnualCertificateIsLate()
    {
        var result = _service.Reconcile(Statements(),
            new[] { Certificate(issued: new DateOnly(2027, 6, 20)) });
        Assert.Contains(result, x => x.Code == "LATE_CERTIFICATE" && x.Severity == "Warning");
        Assert.DoesNotContain(result, x => x.Code == "MATCHED");
    }

    [Fact]
    public void Reconcile_RequiresAllFourConsecutiveQuarters()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.Reconcile(Statements(missingQuarter: 3), Array.Empty<TdsDeducteeCertificate>()));
    }

    [Fact]
    public void Reconcile_RejectsNonSalaryStatements()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.Reconcile(Statements(kind: TdsStatementKind.NonSalaryResident),
                Array.Empty<TdsDeducteeCertificate>()));
    }

    [Fact]
    public void Reconcile_RejectsNullInputs()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _service.Reconcile(null!, Array.Empty<TdsDeducteeCertificate>()));
        Assert.Throws<ArgumentNullException>(() =>
            _service.Reconcile(Statements(), null!));
    }
}
