namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Calculates basic resident-payee withholding for commission or brokerage
/// under section 194H and the corresponding section 393(1), Table Sl. No. 1(ii).
/// The caller must confirm payer eligibility and that the payment is not covered
/// by an excluded/specific commission category (for example, insurance commission).
/// </summary>
public sealed class Tds194HDeductionCalculator
{
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private const decimal AnnualThreshold = 20000m;
    private const decimal RatePercent = 2m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 194H, as amended by Finance Act 2024 and Finance Act 2025; verify applicable notifications and exclusions.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 393(1), Table Sl. No. 1(ii); Income Tax Department notified TDS section-code mapping and transition guidance.";

    public TdsDeductionResult Calculate(Tds194HDeductionRequest request)
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
        var section = newAct ? "Section 393(1), Table Sl. No. 1(ii)" : "Section 194H";
        var source = newAct ? NewActSource : OldActSource;

        TdsDeductionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);

        if (!request.ApplicabilityConfirmed)
            return Review("TDS applicability is unconfirmed. Verify resident-payee status, payer eligibility (including the specified-person test for an individual/HUF), and that the payment is commission/brokerage covered by this provision.");
        if (!request.PanProvided)
            return Review("Deductee PAN is unavailable. Verify the applicable higher-rate provision and exceptions before deduction; this calculator will not guess a rate.");
        if (request.AmountPaidOrCredited == 0)
            return new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, "No payment or credit amount was supplied.", source);

        var cumulative = request.PriorAggregatePaidOrCreditedInFinancialYear + request.AmountPaidOrCredited;
        if (cumulative <= AnnualThreshold)
            return new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m,
                "The financial-year aggregate commission/brokerage paid or credited to this payee has not exceeded ₹20,000.", source);

        var rate = RatePercent;
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
            "Basic section 194H rate applied after the ₹20,000 financial-year aggregate threshold was exceeded. Excluded or separately specified commission categories must be classified under their applicable provisions.",
            source);
    }
}

public sealed record Tds194HDeductionRequest(
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
