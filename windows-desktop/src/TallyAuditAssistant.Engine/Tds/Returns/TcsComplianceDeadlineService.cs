using System;
using System.Collections.Generic;
using System.Linq;

namespace TallyAuditAssistant.Engine.Tds.Returns;

public enum TcsComplianceAct
{
    IncomeTaxAct1961,
    IncomeTaxAct2025
}

public sealed record TcsComplianceDeadline(
    string Code,
    string Description,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly DueDate,
    string Form,
    TcsComplianceAct GoverningAct,
    string LegalReference);

/// <summary>
/// Calculates standard TCS deposit, quarterly statement and collection-certificate
/// deadlines. Extensions, collector-specific orders and portal notices must be checked
/// separately. This service only creates workpaper dates; it never deposits or files.
/// </summary>
public sealed class TcsComplianceDeadlineService
{
    private static readonly DateOnly NewActStart = new(2026, 4, 1);

    public DateOnly GetDepositDueDate(DateOnly collectionDate, bool isGovernmentCollector = false, bool paidWithoutChallan = false)
    {
        if (isGovernmentCollector && paidWithoutChallan)
            return collectionDate;

        if (collectionDate.Month == 3 && !isGovernmentCollector)
            return new DateOnly(collectionDate.Year, 4, 30);

        return new DateOnly(collectionDate.Year, collectionDate.Month, 1).AddMonths(1).AddDays(6);
    }

    public IReadOnlyList<TcsComplianceDeadline> GetQuarterlyDeadlines(int taxYearStartYear)
    {
        if (taxYearStartYear is < 1900 or > 9998)
            throw new ArgumentOutOfRangeException(nameof(taxYearStartYear));

        var periods = new[]
        {
            (Start: new DateOnly(taxYearStartYear, 4, 1), End: new DateOnly(taxYearStartYear, 6, 30)),
            (Start: new DateOnly(taxYearStartYear, 7, 1), End: new DateOnly(taxYearStartYear, 9, 30)),
            (Start: new DateOnly(taxYearStartYear, 10, 1), End: new DateOnly(taxYearStartYear, 12, 31)),
            (Start: new DateOnly(taxYearStartYear + 1, 1, 1), End: new DateOnly(taxYearStartYear + 1, 3, 31))
        };

        var results = new List<TcsComplianceDeadline>(12);
        foreach (var period in periods)
        {
            var act = period.End < NewActStart ? TcsComplianceAct.IncomeTaxAct1961 : TcsComplianceAct.IncomeTaxAct2025;
            var quarterDue = act == TcsComplianceAct.IncomeTaxAct2025
                ? NewQuarterlyStatementDue(period.End)
                : OldQuarterlyStatementDue(period.End);
            var statementForm = act == TcsComplianceAct.IncomeTaxAct2025 ? "Form 143" : "Form 27EQ";
            var certificateForm = act == TcsComplianceAct.IncomeTaxAct2025 ? "Form 133" : "Form 27D";
            var statementRule = act == TcsComplianceAct.IncomeTaxAct2025 ? "Rule 219, Income-tax Rules, 2026" : "Rule 31AA, Income-tax Rules, 1962";
            var certificateRule = act == TcsComplianceAct.IncomeTaxAct2025 ? "Rule 215, Income-tax Rules, 2026" : "Rule 37D, Income-tax Rules, 1962";

            results.Add(new TcsComplianceDeadline(
                "TCS-STATEMENT", "Quarterly TCS collection statement", period.Start, period.End,
                quarterDue, statementForm, act, statementRule));
            results.Add(new TcsComplianceDeadline(
                "TCS-CERTIFICATE", "TCS collection certificate to buyer", period.Start, period.End,
                quarterDue.AddDays(15), certificateForm, act, certificateRule));
        }

        return results.OrderBy(x => x.DueDate).ThenBy(x => x.Code).ToArray();
    }

    private static DateOnly NewQuarterlyStatementDue(DateOnly quarterEnd) =>
        quarterEnd.Month switch
        {
            6 => new DateOnly(quarterEnd.Year, 7, 31),
            9 => new DateOnly(quarterEnd.Year, 10, 31),
            12 => new DateOnly(quarterEnd.Year + 1, 1, 31),
            3 => new DateOnly(quarterEnd.Year, 5, 31),
            _ => throw new ArgumentOutOfRangeException(nameof(quarterEnd))
        };

    private static DateOnly OldQuarterlyStatementDue(DateOnly quarterEnd) =>
        quarterEnd.Month switch
        {
            6 => new DateOnly(quarterEnd.Year, 7, 15),
            9 => new DateOnly(quarterEnd.Year, 10, 15),
            12 => new DateOnly(quarterEnd.Year + 1, 1, 15),
            3 => new DateOnly(quarterEnd.Year, 5, 15),
            _ => throw new ArgumentOutOfRangeException(nameof(quarterEnd))
        };
}
