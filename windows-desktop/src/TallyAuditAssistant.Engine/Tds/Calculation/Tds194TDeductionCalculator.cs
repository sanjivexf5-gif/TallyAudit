namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Calculates TDS on salary, remuneration, commission, bonus and interest paid
/// or credited by a firm to its partner under section 194T / section 393(3),
/// Table Sl. No. 7. The caller must supply the complete partner-year aggregate.
/// </summary>
public sealed class Tds194TDeductionCalculator
{
    private static readonly DateOnly EffectiveDate = new(2025, 4, 1);
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private const decimal Threshold = 20000m;
    private const decimal RatePercent = 10m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 194T, inserted by Finance (No. 2) Act, 2024, effective 1 April 2025; section 197 and section 206AA, as applicable.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 393(3), Table Sl. No. 7, read with applicable provisions of section 397; corresponding rules and Finance Act, 2026.";

    public TdsDeductionResult Calculate(Tds194TDeductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CreditOrPaymentDate is null)
            throw new ArgumentException("The earlier credit/payment date is required.", nameof(request));
        if (request.CurrentPaymentAmount < 0m || request.PriorAggregateEligiblePaymentsInYear < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Payment values cannot be negative.");

        var date = request.CreditOrPaymentDate.Value;
        var newAct = date >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = newAct
            ? "Section 393(3), Table Sl. No. 7"
            : "Section 194T";
        var source = newAct ? NewActSource : OldActSource;

        TdsDeductionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);
        TdsDeductionResult NotApplicable(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, source);

        if (date < EffectiveDate)
            return NotApplicable("Section 194T was not effective for credit/payment events before 1 April 2025. Review the applicable earlier TDS provisions separately.");
        if (!request.FirmIsPayer || !request.RecipientIsPartnerOfPayingFirm || !request.ApplicabilityConfirmed)
            return Review("Confirm that the payer is the firm and the recipient is its partner, and that the amount is salary, remuneration, commission, bonus or interest covered by this provision. Profit share, drawings and other unlisted credits must not be classified as covered sums without legal review.");
        if (!request.IsCoveredPaymentCategory)
            return NotApplicable("The supplied amount is not classified as salary, remuneration, commission, bonus or interest payable to the partner.");
        if (request.CurrentPaymentAmount == 0m)
            return NotApplicable("No covered payment amount was supplied.");

        var aggregate = request.PriorAggregateEligiblePaymentsInYear + request.CurrentPaymentAmount;
        if (aggregate <= Threshold)
            return NotApplicable($"The partner-year aggregate covered payments of ₹{aggregate:N2} do not exceed the ₹20,000 threshold.");

        if (!request.PanProvided)
            return Review("PAN is unavailable. Verify the applicable higher-rate provision and any statutory exception before deduction; this calculator deliberately does not guess the rate.");

        var rate = RatePercent;
        if (request.LowerDeductionCertificateRatePercent is decimal certificateRate)
        {
            if (string.IsNullOrWhiteSpace(request.LowerDeductionCertificateReference) ||
                request.CertificateValidFrom is null || request.CertificateValidTo is null ||
                date < request.CertificateValidFrom || date > request.CertificateValidTo ||
                certificateRate < 0m || certificateRate > rate)
                return Review("Lower/nil deduction certificate details are incomplete, invalid, outside their validity period, or exceed the ordinary section rate.");
            rate = certificateRate;
        }

        var tax = decimal.Round(aggregate * rate / 100m, 2, MidpointRounding.AwayFromZero);
        return new(TdsCalculationStatus.Calculated, act, section, rate, aggregate, tax,
            $"{rate}% applies to the full partner-year aggregate of ₹{aggregate:N2} once it exceeds ₹20,000; it is not limited to the excess. The earlier of credit (including credit to the partner's capital account) or payment determines the deduction date. The caller must include all covered categories for this partner across the firm for the financial year and retain ledger/category evidence.",
            source);
    }
}

public sealed record Tds194TDeductionRequest(
    DateOnly? CreditOrPaymentDate,
    decimal CurrentPaymentAmount,
    decimal PriorAggregateEligiblePaymentsInYear,
    bool FirmIsPayer,
    bool RecipientIsPartnerOfPayingFirm,
    bool IsCoveredPaymentCategory,
    bool ApplicabilityConfirmed,
    bool PanProvided,
    decimal? LowerDeductionCertificateRatePercent = null,
    string? LowerDeductionCertificateReference = null,
    DateOnly? CertificateValidFrom = null,
    DateOnly? CertificateValidTo = null);
