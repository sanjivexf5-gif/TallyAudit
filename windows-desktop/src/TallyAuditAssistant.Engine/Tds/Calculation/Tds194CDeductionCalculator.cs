namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Calculates the basic resident-contractor withholding under section 194C / the
/// corresponding 2025 Act table entry. It deliberately requires confirmed
/// applicability and escalates PAN/certificate edge cases it cannot safely resolve.
/// </summary>
public sealed class Tds194CDeductionCalculator
{
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private const decimal SinglePaymentThreshold = 30000m;
    private const decimal AnnualAggregateThreshold = 100000m;
    private const decimal IndividualOrHufRate = 1m;
    private const decimal OtherEntityRate = 2m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 194C; Income Tax Department section 194C and threshold-limit guidance.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 393(1), Table Sl. No. 6(i); Income Tax Department TDS Rates (TY 2026-27) and TDS Compliance FAQs.";

    public TdsDeductionResult Calculate(TdsDeductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CreditDate is null && request.PaymentDate is null)
            throw new ArgumentException("At least one of credit date or payment date is required.", nameof(request));
        if (request.AmountPaidOrCredited < 0 || request.PriorAggregatePaidOrCreditedInFinancialYear < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Payment and prior aggregate amounts cannot be negative.");

        var triggerDate = request.CreditDate is null ? request.PaymentDate!.Value
            : request.PaymentDate is null ? request.CreditDate.Value
            : request.CreditDate.Value <= request.PaymentDate.Value ? request.CreditDate.Value : request.PaymentDate.Value;
        var newAct = triggerDate >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = newAct ? "Section 393(1), Table Sl. No. 6(i)" : "Section 194C";
        var source = newAct ? NewActSource : OldActSource;

        TdsDeductionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);

        if (!request.ApplicabilityConfirmed)
            return Review("TDS applicability has not been confirmed. Verify payer status, resident contractor status, contract nature and statutory exclusions before calculating.");
        if (!request.PanProvided)
            return Review("Deductee PAN is unavailable. Apply the applicable higher-rate provision only after verifying the current Act, exceptions and supporting records; this calculator will not guess the rate.");
        if (request.AmountPaidOrCredited == 0)
            return new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, "No payment or credit amount was supplied.", source);

        var cumulative = request.PriorAggregatePaidOrCreditedInFinancialYear + request.AmountPaidOrCredited;
        if (request.AmountPaidOrCredited <= SinglePaymentThreshold && cumulative <= AnnualAggregateThreshold)
            return new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m,
                "Neither the single-payment threshold nor the financial-year aggregate threshold has been exceeded.", source);

        var rate = request.DeducteeEntityType == TdsDeducteeEntityType.IndividualOrHuf
            ? IndividualOrHufRate
            : OtherEntityRate;

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
        return new(TdsCalculationStatus.Calculated, act, section, rate, request.AmountPaidOrCredited, tax,
            "Basic section 194C contractor rate applied to the current payment/credit after the threshold condition was met. Verify invoice material-value treatment, transport declarations, payer exclusions, surcharge/cess and any other applicable provisions separately.",
            source);
    }
}
