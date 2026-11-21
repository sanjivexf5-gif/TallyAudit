using System;

namespace TallyAuditAssistant.Engine.Tds.Returns;

public enum TdsCertificateKind
{
    Salary,
    NonSalary
}

public enum TdsCertificateRegime
{
    IncomeTaxAct1961,
    IncomeTaxAct2025
}

public sealed record TdsCertificateDeadline(
    TdsCertificateKind Kind,
    TdsCertificateRegime Regime,
    string FormNumber,
    DateOnly ReportingPeriodStart,
    DateOnly ReportingPeriodEnd,
    DateOnly DueDate,
    string LegalReference,
    string Explanation);

/// <summary>
/// Resolves standard salary and non-salary TDS certificate forms and issue deadlines.
/// Special challan-cum-statement certificates (old Forms 16B-16E / new Form 132)
/// are intentionally outside this service and require transaction-specific rules.
/// This service is a compliance workpaper only; it does not generate, sign, download or issue certificates.
/// </summary>
public sealed class TdsCertificateDeadlineService
{
    private static readonly DateOnly NewActStart = new(2026, 4, 1);

    public TdsCertificateDeadline ResolveSalary(DateOnly financialYearStart)
    {
        if (financialYearStart.Month != 4 || financialYearStart.Day != 1)
            throw new ArgumentException("Salary certificate period must start on 1 April.", nameof(financialYearStart));
        if (financialYearStart.Year < 1900 || financialYearStart.Year > 9998)
            throw new ArgumentOutOfRangeException(nameof(financialYearStart));

        var periodEnd = financialYearStart.AddYears(1).AddDays(-1);
        var regime = periodEnd < NewActStart
            ? TdsCertificateRegime.IncomeTaxAct1961
            : TdsCertificateRegime.IncomeTaxAct2025;
        var form = regime == TdsCertificateRegime.IncomeTaxAct1961 ? "16" : "130";
        var dueDate = new DateOnly(financialYearStart.Year + 1, 6, 15);

        return new TdsCertificateDeadline(TdsCertificateKind.Salary, regime, form,
            financialYearStart, periodEnd, dueDate,
            regime == TdsCertificateRegime.IncomeTaxAct1961 ? "Income-tax Rules, 1962, Rule 31" : "Income-tax Rules, 2026, Rule 215",
            $"Annual salary TDS certificate Form {form} is due by 15 June following the financial year.");
    }

    public TdsCertificateDeadline ResolveNonSalaryQuarter(DateOnly quarterStart, DateOnly quarterEnd)
    {
        ValidateQuarter(quarterStart, quarterEnd);
        var regime = quarterEnd < NewActStart
            ? TdsCertificateRegime.IncomeTaxAct1961
            : TdsCertificateRegime.IncomeTaxAct2025;
        var form = regime == TdsCertificateRegime.IncomeTaxAct1961 ? "16A" : "131";
        var statementDueDate = GetQuarterlyStatementDueDate(quarterEnd);
        var certificateDueDate = statementDueDate.AddDays(15);

        return new TdsCertificateDeadline(TdsCertificateKind.NonSalary, regime, form,
            quarterStart, quarterEnd, certificateDueDate,
            regime == TdsCertificateRegime.IncomeTaxAct1961 ? "Income-tax Rules, 1962, Rule 31" : "Income-tax Rules, 2026, Rule 215",
            $"Quarterly non-salary TDS certificate Form {form} is due within 15 days after the quarterly TDS statement due date ({statementDueDate:dd MMM yyyy}).");
    }

    private static DateOnly GetQuarterlyStatementDueDate(DateOnly quarterEnd) => quarterEnd switch
    {
        { Month: 6, Day: 30 } => new DateOnly(quarterEnd.Year, 7, 31),
        { Month: 9, Day: 30 } => new DateOnly(quarterEnd.Year, 10, 31),
        { Month: 12, Day: 31 } => new DateOnly(quarterEnd.Year + 1, 1, 31),
        { Month: 3, Day: 31 } => new DateOnly(quarterEnd.Year, 5, 31),
        _ => throw new ArgumentException("Quarter end must be 30 June, 30 September, 31 December or 31 March.", nameof(quarterEnd))
    };

    private static void ValidateQuarter(DateOnly start, DateOnly end)
    {
        if (start > end)
            throw new ArgumentException("Quarter start must not exceed quarter end.");
        var expectedStart = end.Month switch
        {
            6 => new DateOnly(end.Year, 4, 1),
            9 => new DateOnly(end.Year, 7, 1),
            12 => new DateOnly(end.Year, 10, 1),
            3 => new DateOnly(end.Year, 1, 1),
            _ => throw new ArgumentException("Quarter end must be a statutory quarter end.", nameof(end))
        };
        if (start != expectedStart || end != expectedStart.AddMonths(3).AddDays(-1))
            throw new ArgumentException("The supplied dates do not represent one complete Indian financial-year quarter.");
        if (start < NewActStart && end >= NewActStart)
            throw new ArgumentException("A quarterly certificate period cannot span the 1 April 2026 legislative transition.");
    }
}
