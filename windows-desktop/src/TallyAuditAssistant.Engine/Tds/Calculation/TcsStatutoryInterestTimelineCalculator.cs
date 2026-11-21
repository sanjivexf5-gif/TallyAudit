using System;
using System.Collections.Generic;

namespace TallyAuditAssistant.Engine.Tds.Calculation;

public enum TcsInterestRegime
{
    IncomeTaxAct1961BeforeApril2025Amendment,
    IncomeTaxAct1961FromApril2025,
    IncomeTaxAct2025
}

public sealed record TcsStatutoryInterestRequest(
    TcsInterestRegime Regime,
    decimal TaxAmount,
    DateOnly CollectibleDate,
    DateOnly? CollectedDate,
    DateOnly PaidDate);

public sealed record TcsInterestStageResult(
    string Stage,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal MonthlyRatePercent,
    int MonthsOrParts,
    decimal InterestAmount);

public sealed record TcsStatutoryInterestResult(
    TcsInterestRegime Regime,
    decimal TaxAmount,
    decimal TotalInterest,
    IReadOnlyList<TcsInterestStageResult> Stages,
    string SectionReference,
    string Explanation,
    string SourceReference);

/// <summary>
/// Calculates TCS simple interest once across the collection and deposit timeline.
/// This is a review workpaper; it does not create a demand, pay tax or file a statement.
/// Dates are inclusive for calendar-month-or-part counting, consistent with the statutory
/// phrase "for every month or part thereof". Relief under buyer-return-furnished provisos
/// and AO-specific directions must be reviewed separately.
/// </summary>
public sealed class TcsStatutoryInterestTimelineCalculator
{
    private static readonly DateOnly AmendmentStart = new(2025, 4, 1);
    private static readonly DateOnly NewActStart = new(2026, 4, 1);

    public TcsStatutoryInterestResult Calculate(TcsStatutoryInterestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TaxAmount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Tax amount must be greater than zero.");
        if (request.PaidDate < request.CollectibleDate)
            throw new ArgumentException("Payment date cannot be earlier than the collectible date.", nameof(request));
        if (request.CollectedDate is { } collected &&
            (collected < request.CollectibleDate || collected > request.PaidDate))
            throw new ArgumentException("Collection date must be on or after the collectible date and on or before payment.", nameof(request));

        ValidateRegimeDates(request);
        var stages = new List<TcsInterestStageResult>();

        if (request.Regime == TcsInterestRegime.IncomeTaxAct1961BeforeApril2025Amendment)
        {
            AddStage(stages, "Collectible to actual payment (pre-amendment single stage)",
                request.CollectibleDate, request.PaidDate, 1.0m, request.TaxAmount);
        }
        else
        {
            if (request.CollectedDate is not { } collectionDate)
                throw new ArgumentException("A collection date is required for the post-amendment interest calculation.", nameof(request));

            AddStage(stages, "Failure to collect", request.CollectibleDate, collectionDate, 1.0m, request.TaxAmount);
            AddStage(stages, "Failure to deposit after collection", collectionDate, request.PaidDate, 1.5m, request.TaxAmount);
        }

        var total = decimal.Round(SumInterest(stages), 2, MidpointRounding.AwayFromZero);
        var section = request.Regime switch
        {
            TcsInterestRegime.IncomeTaxAct1961BeforeApril2025Amendment or
            TcsInterestRegime.IncomeTaxAct1961FromApril2025 => "Section 206C(7), Income-tax Act, 1961",
            TcsInterestRegime.IncomeTaxAct2025 => "Section 398(3)(a), Income-tax Act, 2025",
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Unsupported interest regime.")
        };
        var explanation = request.Regime == TcsInterestRegime.IncomeTaxAct1961BeforeApril2025Amendment
            ? "Pre-1 April 2025 rule: 1% simple interest for each calendar month or part from the collectible date to actual payment."
            : "Post-amendment rule: 1% simple interest for each calendar month or part from collectible date to collection, plus 1.5% for each calendar month or part from collection to actual payment. Stage periods are calculated separately; review any relief or assessing-officer direction.";
        var source = request.Regime == TcsInterestRegime.IncomeTaxAct2025
            ? "Income-tax Act, 2025, Section 398(3)(a)(i)-(ii)"
            : "Income-tax Act, 1961, Section 206C(7), as applicable for the selected period";

        return new TcsStatutoryInterestResult(request.Regime, request.TaxAmount, total,
            stages.AsReadOnly(), section, explanation, source);
    }

    private static void ValidateRegimeDates(TcsStatutoryInterestRequest request)
    {
        var valid = request.Regime switch
        {
            TcsInterestRegime.IncomeTaxAct1961BeforeApril2025Amendment =>
                request.CollectibleDate < AmendmentStart && request.PaidDate < AmendmentStart,
            TcsInterestRegime.IncomeTaxAct1961FromApril2025 =>
                request.CollectibleDate >= AmendmentStart && request.PaidDate < NewActStart,
            TcsInterestRegime.IncomeTaxAct2025 =>
                request.CollectibleDate >= NewActStart,
            _ => false
        };
        if (!valid)
            throw new ArgumentException("The dates cross or do not belong to the selected statutory interest regime. Split the workpaper at the applicable legal transition and obtain review before applying a rate.", nameof(request));
    }

    private static void AddStage(ICollection<TcsInterestStageResult> stages, string name,
        DateOnly start, DateOnly end, decimal rate, decimal principal)
    {
        var months = checked((end.Year - start.Year) * 12 + end.Month - start.Month + 1);
        var interest = decimal.Round(principal * rate / 100m * months, 2, MidpointRounding.AwayFromZero);
        stages.Add(new TcsInterestStageResult(name, start, end, rate, months, interest));
    }

    private static decimal SumInterest(IEnumerable<TcsInterestStageResult> stages)
    {
        var total = 0m;
        foreach (var stage in stages)
            total += stage.InterestAmount;
        return total;
    }
}
