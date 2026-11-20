namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Calculates withholding on commission, remuneration or prize paid to a
/// lottery-ticket stockist, distributor, purchaser or seller under section 194G
/// and the corresponding section 393(3), Table Sl. No. 4.
/// </summary>
public sealed class Tds194GDeductionCalculator
{
    private static readonly DateOnly RateChangeDate = new(2024, 10, 1);
    private static readonly DateOnly ThresholdChangeDate = new(2025, 4, 1);
    private static readonly DateOnly NewActStart = new(2026, 4, 1);

    private const decimal EarlierRatePercent = 5m;
    private const decimal CurrentRatePercent = 2m;
    private const decimal EarlierAnnualThreshold = 15000m;
    private const decimal CurrentAnnualThreshold = 20000m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 194G, as amended by Finance (No. 2) Act, 2024 (rate from 1 October 2024) and Finance Act, 2025 (threshold from 1 April 2025); verify applicable sections 197 and 206AA.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 393(3), Table Sl. No. 4, read with applicable provisions of section 397 and the Income-tax Rules, 2026.";

    public TdsDeductionResult Calculate(Tds194GDeductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CreditDate is null && request.PaymentDate is null)
            throw new ArgumentException("At least one of credit date or payment date is required.", nameof(request));
        if (request.AmountPaidOrCredited < 0m || request.PriorAggregatePaidOrCreditedInFinancialYear < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Payment and prior aggregate amounts cannot be negative.");

        var triggerDate = request.CreditDate is null ? request.PaymentDate!.Value
            : request.PaymentDate is null ? request.CreditDate.Value
            : request.CreditDate.Value <= request.PaymentDate.Value ? request.CreditDate.Value : request.PaymentDate.Value;

        var newAct = triggerDate >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = newAct ? "Section 393(3), Table Sl. No. 4" : "Section 194G";
        var source = newAct ? NewActSource : OldActSource;
        var threshold = !newAct && triggerDate < ThresholdChangeDate
            ? EarlierAnnualThreshold
            : CurrentAnnualThreshold;
        var rate = !newAct && triggerDate < RateChangeDate
            ? EarlierRatePercent
            : CurrentRatePercent;

        TdsDeductionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);

        TdsDeductionResult BelowThreshold(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, source);

        if (!request.ApplicabilityConfirmed)
            return Review("Confirm that the recipient is or has been stocking, distributing, purchasing or selling lottery tickets and that the payment is commission, remuneration or prize on those tickets. Do not use this section for a lottery-ticket buyer's winnings or unrelated agency commission.");
        if (request.AmountPaidOrCredited == 0m)
            return BelowThreshold("No commission, remuneration or prize payment/credit amount was supplied.");
        if (!request.PanProvided)
            return Review("Deductee PAN is unavailable. Verify the applicable higher-rate provision and any statutory exception before deduction; this calculator deliberately does not assume a PAN-default rate.");

        var aggregate = request.PriorAggregatePaidOrCreditedInFinancialYear + request.AmountPaidOrCredited;
        if (aggregate <= threshold)
            return BelowThreshold($"The financial-year aggregate of covered lottery-ticket commission, remuneration or prize is ₹{aggregate:N2}, which does not exceed the applicable ₹{threshold:N0} threshold.");

        if (request.LowerDeductionCertificateRatePercent is decimal certificateRate)
        {
            if (string.IsNullOrWhiteSpace(request.LowerDeductionCertificateReference) ||
                request.CertificateValidFrom is null || request.CertificateValidTo is null ||
                triggerDate < request.CertificateValidFrom || triggerDate > request.CertificateValidTo ||
                certificateRate < 0m || certificateRate > rate)
                return Review("Lower/nil deduction certificate details are incomplete, invalid, outside their validity period, or exceed the ordinary section rate.");
            rate = certificateRate;
        }

        var tax = decimal.Round(request.AmountPaidOrCredited * rate / 100m, 2, MidpointRounding.AwayFromZero);
        var ratePeriod = !newAct && triggerDate < RateChangeDate
            ? "The historical 5% rate applies before 1 October 2024."
            : "The 2% rate applies from 1 October 2024.";
        var thresholdPeriod = !newAct && triggerDate < ThresholdChangeDate
            ? "The historical annual threshold is ₹15,000 before 1 April 2025."
            : "The annual threshold is ₹20,000 from 1 April 2025.";
        return new(TdsCalculationStatus.Calculated, act, section, rate, request.AmountPaidOrCredited, tax,
            $"The aggregate of ₹{aggregate:N2} exceeded the applicable annual threshold. TDS is calculated on this current credit/payment event of ₹{request.AmountPaidOrCredited:N2}, not on the prior-year-to-date amount again. {ratePeriod} {thresholdPeriod} Deduct at the earlier of credit (including suspense-account credit) or payment. Confirm payee identity, annual aggregation and certificate/PAN evidence before posting.",
            source);
    }
}

public sealed record Tds194GDeductionRequest(
    DateOnly? CreditDate,
    DateOnly? PaymentDate,
    decimal AmountPaidOrCredited,
    decimal PriorAggregatePaidOrCreditedInFinancialYear,
    bool PanProvided,
    bool ApplicabilityConfirmed,
    decimal? LowerDeductionCertificateRatePercent = null,
    string? LowerDeductionCertificateReference = null,
    DateOnly? CertificateValidFrom = null,
    DateOnly? CertificateValidTo = null);
