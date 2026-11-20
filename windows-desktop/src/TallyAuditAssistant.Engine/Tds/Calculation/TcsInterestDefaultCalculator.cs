using System;

namespace TallyAuditAssistant.Engine.Tds.Calculation;

public enum TcsInterestDefaultType
{
    FailureToCollect,
    FailureToDeposit
}

public enum TcsInterestGoverningAct
{
    IncomeTaxAct1961,
    IncomeTaxAct2025
}

public sealed record TcsInterestDefaultRequest(
    TcsInterestDefaultType DefaultType,
    TcsInterestGoverningAct GoverningAct,
    decimal TaxAmount,
    DateOnly InterestStartDate,
    DateOnly ResolutionDate);

public sealed record TcsInterestDefaultResult(
    TcsInterestDefaultType DefaultType,
    TcsInterestGoverningAct GoverningAct,
    decimal TaxAmount,
    decimal MonthlyRatePercent,
    int MonthsOrParts,
    decimal InterestAmount,
    DateOnly InterestStartDate,
    DateOnly ResolutionDate,
    string SectionReference,
    string Explanation,
    string SourceReference);

/// <summary>
/// Calculates simple interest for TCS collection/deposit defaults.
/// It is a review workpaper only and does not create a demand, pay tax or file a statement.
/// </summary>
public sealed class TcsInterestDefaultCalculator
{
    public TcsInterestDefaultResult Calculate(TcsInterestDefaultRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TaxAmount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Tax amount must be greater than zero.");
        if (request.ResolutionDate < request.InterestStartDate)
            throw new ArgumentException("Resolution date cannot be earlier than the interest start date.", nameof(request));

        var months = CountCalendarMonthsOrParts(request.InterestStartDate, request.ResolutionDate);
        decimal rateTotal = 0m;
        decimal interestTotal = 0m;
        var month = new DateOnly(request.InterestStartDate.Year, request.InterestStartDate.Month, 1);
        var finalMonth = new DateOnly(request.ResolutionDate.Year, request.ResolutionDate.Month, 1);

        while (month <= finalMonth)
        {
            var monthlyRate = GetMonthlyRate(request, month);
            rateTotal += monthlyRate;
            interestTotal += request.TaxAmount * monthlyRate / 100m;
            month = month.AddMonths(1);
        }

        var averageMonthlyRate = rateTotal / months;
        var interest = decimal.Round(interestTotal, 2, MidpointRounding.AwayFromZero);
        var section = request.GoverningAct switch
        {
            TcsInterestGoverningAct.IncomeTaxAct1961 => "Section 206C(7), Income-tax Act, 1961",
            TcsInterestGoverningAct.IncomeTaxAct2025 => "Section 398(3)(a), Income-tax Act, 2025",
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Unsupported governing Act.")
        };

        var defaultDescription = request.DefaultType == TcsInterestDefaultType.FailureToCollect
            ? "Failure to collect TCS: 1% for every month or part of a month from the date TCS was collectible until it was collected."
            : "Failure to deposit collected TCS: 1% per month or part before 1 April 2025 and 1.5% per month or part from 1 April 2025 under the 1961 Act; the 2025 Act uses the 1.5% deposit rate.";

        return new TcsInterestDefaultResult(
            request.DefaultType,
            request.GoverningAct,
            request.TaxAmount,
            averageMonthlyRate,
            months,
            interest,
            request.InterestStartDate,
            request.ResolutionDate,
            section,
            $"{defaultDescription} {months} month(s) or part(s) applied; interest is simple interest.",
            request.GoverningAct == TcsInterestGoverningAct.IncomeTaxAct2025
                ? "Income-tax Act, 2025, Section 398(3)(a)(i)-(ii)"
                : "Income-tax Act, 1961, Section 206C(7)");
    }

    private static decimal GetMonthlyRate(TcsInterestDefaultRequest request, DateOnly month)
    {
        if (request.DefaultType == TcsInterestDefaultType.FailureToCollect)
            return 1.0m;

        if (request.GoverningAct == TcsInterestGoverningAct.IncomeTaxAct1961 && month < new DateOnly(2025, 4, 1))
            return 1.0m;

        return 1.5m;
    }

    private static int CountCalendarMonthsOrParts(DateOnly start, DateOnly end)
    {
        // The statutory phrase is "every month or part of a month". Count each
        // calendar month touched by the inclusive default period.
        return checked((end.Year - start.Year) * 12 + end.Month - start.Month + 1);
    }
}
