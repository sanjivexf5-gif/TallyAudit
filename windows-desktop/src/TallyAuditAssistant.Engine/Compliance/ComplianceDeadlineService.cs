using System;
using System.Collections.Generic;
using System.Linq;

namespace TallyAuditAssistant.Engine.Compliance;

/// <summary>
/// A configurable recurring compliance reminder rule. PeriodMonthOverride supports
/// cases such as the March TDS deposit, whose ordinary next-month day is overridden.
/// </summary>
public sealed record ComplianceDeadlineRule(
    string Code,
    string Name,
    string Regime,
    int DueDay,
    int DueMonthOffset,
    string Description,
    string SourceReference,
    int? SpecialPeriodMonth = null,
    int? SpecialDueDay = null,
    int? SpecialDueMonthOffset = null);

public sealed record ComplianceDeadlineItem(
    string Code,
    string Name,
    string Regime,
    string TaxPeriod,
    DateOnly DueDate,
    string Status,
    int DaysRemaining,
    string Description,
    string SourceReference)
{
    public string DueDateDisplay => DueDate.ToString("dd-MMM-yyyy");
    public string DaysDisplay => DaysRemaining switch
    {
        < 0 => $"{Math.Abs(DaysRemaining)} day(s) overdue",
        0 => "Due today",
        1 => "1 day remaining",
        _ => $"{DaysRemaining} days remaining"
    };
}

/// <summary>
/// Produces local compliance reminders. Dates are planning defaults only: notifications,
/// taxpayer category, jurisdiction and portal extensions can change statutory due dates.
/// No return is filed and no payment is initiated.
/// </summary>
public sealed class ComplianceDeadlineService
{
    private static readonly IReadOnlyList<ComplianceDeadlineRule> DefaultRules =
        new[]
        {
            new ComplianceDeadlineRule(
                "GST-GSTR1-M", "GSTR-1 (monthly)", "GST", 11, 1,
                "Default monthly filer reminder. QRMP taxpayers and notified extensions have different timelines.",
                "https://www.gst.gov.in/"),
            new ComplianceDeadlineRule(
                "GST-GSTR3B-M", "GSTR-3B (monthly)", "GST", 20, 1,
                "Default monthly filer reminder. State-wise due dates and taxpayer schemes may differ.",
                "https://www.gst.gov.in/"),
            new ComplianceDeadlineRule(
                "TDS-DEPOSIT-M", "TDS deposit", "TDS", 7, 1,
                "Monthly reminder; tax deducted in March is generally deposited by 30 April.",
                "https://www.incometax.gov.in/",
                SpecialPeriodMonth: 3, SpecialDueDay: 30, SpecialDueMonthOffset: 1)
        };

    public IReadOnlyList<ComplianceDeadlineRule> GetDefaultRules() => DefaultRules;

    public IReadOnlyList<ComplianceDeadlineItem> GenerateUpcoming(
        DateTime asOfDate,
        int monthsAhead = 3,
        IEnumerable<ComplianceDeadlineRule>? rules = null)
    {
        if (monthsAhead is < 0 or > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(monthsAhead), "The reminder horizon must be between 0 and 24 months.");
        }

        var selectedRules = (rules ?? DefaultRules).ToArray();
        foreach (var rule in selectedRules)
        {
            ValidateRule(rule);
        }

        var asOf = DateOnly.FromDateTime(asOfDate);
        var firstPeriod = new DateOnly(asOf.Year, asOf.Month, 1).AddMonths(-1);
        var lastDueDate = asOf.AddMonths(monthsAhead);
        var results = new List<ComplianceDeadlineItem>();

        for (var period = firstPeriod; period <= new DateOnly(lastDueDate.Year, lastDueDate.Month, 1); period = period.AddMonths(1))
        {
            foreach (var rule in selectedRules)
            {
                var isSpecial = rule.SpecialPeriodMonth == period.Month;
                var dueDay = isSpecial ? rule.SpecialDueDay!.Value : rule.DueDay;
                var monthOffset = isSpecial ? rule.SpecialDueMonthOffset!.Value : rule.DueMonthOffset;
                var dueMonth = period.AddMonths(monthOffset);
                var dueDate = new DateOnly(dueMonth.Year, dueMonth.Month, Math.Min(dueDay, DateTime.DaysInMonth(dueMonth.Year, dueMonth.Month)));

                if (dueDate < asOf.AddMonths(-1) || dueDate > lastDueDate)
                {
                    continue;
                }

                var daysRemaining = dueDate.DayNumber - asOf.DayNumber;
                var status = daysRemaining < 0 ? "Overdue"
                    : daysRemaining == 0 ? "Due today"
                    : daysRemaining <= 7 ? "Due soon"
                    : "Upcoming";

                results.Add(new ComplianceDeadlineItem(
                    rule.Code, rule.Name, rule.Regime, period.ToString("MMM yyyy"),
                    dueDate, status, daysRemaining, rule.Description, rule.SourceReference));
            }
        }

        return results.OrderBy(x => x.DueDate).ThenBy(x => x.Regime).ThenBy(x => x.Name).ToArray();
    }

    private static void ValidateRule(ComplianceDeadlineRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (string.IsNullOrWhiteSpace(rule.Code) || string.IsNullOrWhiteSpace(rule.Name) ||
            string.IsNullOrWhiteSpace(rule.Regime))
        {
            throw new ArgumentException("A compliance rule must have a code, name and regime.", nameof(rule));
        }

        if (rule.DueDay is < 1 or > 31 || rule.DueMonthOffset is < 0 or > 12)
        {
            throw new ArgumentException($"Compliance rule '{rule.Code}' has an invalid due day or month offset.", nameof(rule));
        }

        var hasAnySpecial = rule.SpecialPeriodMonth.HasValue || rule.SpecialDueDay.HasValue || rule.SpecialDueMonthOffset.HasValue;
        var hasAllSpecial = rule.SpecialPeriodMonth.HasValue && rule.SpecialDueDay.HasValue && rule.SpecialDueMonthOffset.HasValue;
        if (hasAnySpecial && (!hasAllSpecial || rule.SpecialPeriodMonth is < 1 or > 12 ||
            rule.SpecialDueDay is < 1 or > 31 || rule.SpecialDueMonthOffset is < 0 or > 12))
        {
            throw new ArgumentException($"Compliance rule '{rule.Code}' has an incomplete or invalid special-period override.", nameof(rule));
        }
    }
}
