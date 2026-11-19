namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Calculates the basic resident-payee withholding for section 194J and its
/// corresponding entry in section 393(1), Table Sl. No. 6(iii).
/// Applicability must be confirmed by the caller, including payer category,
/// payee residency and the nature of the service/payment.
/// </summary>
public sealed class Tds194JDeductionCalculator
{
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private const decimal AnnualThreshold = 50000m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 194J, as amended by Finance Act 2025; Income Tax Department section 194J guidance.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 393(1), Table Sl. No. 6(iii); Income Tax Department TDS Rates (Tax Year 2026-27).";

    public TdsDeductionResult Calculate(Tds194JDeductionRequest request)
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
        var section = newAct ? "Section 393(1), Table Sl. No. 6(iii)" : "Section 194J";
        var source = newAct ? NewActSource : OldActSource;

        TdsDeductionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);

        if (!request.ApplicabilityConfirmed)
            return Review("TDS applicability is unconfirmed. Verify resident-payee status, payer eligibility (including the specified-person/turnover test for an individual or HUF), payment classification and statutory exclusions.");
        if (!request.PanProvided)
            return Review("Deductee PAN is unavailable. Verify the applicable higher-rate provision and exceptions before deduction; this calculator will not guess a rate.");
        if (request.AmountPaidOrCredited == 0)
            return new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, "No payment or credit amount was supplied.", source);

        var rate = request.PaymentCategory switch
        {
            Tds194JPaymentCategory.TechnicalServices or
            Tds194JPaymentCategory.CinematographicFilmRoyalty or
            Tds194JPaymentCategory.CallCentreServices => 2m,
            Tds194JPaymentCategory.ProfessionalServices or
            Tds194JPaymentCategory.OtherRoyalty or
            Tds194JPaymentCategory.DirectorRemuneration or
            Tds194JPaymentCategory.Section28VaNonCompeteFee => 10m,
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Unsupported section 194J payment category.")
        };

        var hasThreshold = request.PaymentCategory != Tds194JPaymentCategory.DirectorRemuneration;
        var cumulative = request.PriorAggregatePaidOrCreditedInFinancialYear + request.AmountPaidOrCredited;
        if (hasThreshold && cumulative <= AnnualThreshold)
            return new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m,
                "The financial-year aggregate for this payee/category has not exceeded ₹50,000.", source);

        if (request.LowerDeductionCertificateRatePercent is decimal certificateRate)
        {
            if (string.IsNullOrWhiteSpace(request.LowerDeductionCertificateReference) ||
                request.CertificateValidFrom is null || request.CertificateValidTo is null ||
                triggerDate < request.CertificateValidFrom || triggerDate > request.CertificateValidTo ||
                certificateRate < 0m || certificateRate > rate)
                return Review("Lower/nil deduction certificate details are incomplete, invalid, outside their validity period, or exceed the ordinary category rate.");
            rate = certificateRate;
        }

        var tax = decimal.Round(request.AmountPaidOrCredited * rate / 100m, 2, MidpointRounding.AwayFromZero);
        return new(TdsCalculationStatus.Calculated, act, section, rate, request.AmountPaidOrCredited, tax,
            hasThreshold
                ? "Section 194J category rate applied after the ₹50,000 financial-year aggregate threshold was exceeded."
                : "Director remuneration is subject to deduction without a monetary threshold; category rate applied.",
            source);
    }
}

public sealed record Tds194JDeductionRequest(
    DateOnly? CreditDate,
    DateOnly? PaymentDate,
    decimal AmountPaidOrCredited,
    decimal PriorAggregatePaidOrCreditedInFinancialYear,
    Tds194JPaymentCategory PaymentCategory,
    bool PanProvided,
    bool ApplicabilityConfirmed,
    decimal? LowerDeductionCertificateRatePercent = null,
    string? LowerDeductionCertificateReference = null,
    DateOnly? CertificateValidFrom = null,
    DateOnly? CertificateValidTo = null);
