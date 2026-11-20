namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Calculates TDS on specified contract, professional-fee and commission/brokerage
/// payments by an Individual/HUF under section 194M / section 393(1), Table Sl. No. 6(ii).
/// The caller supplies the complete payer-payee aggregate for the financial/tax year.
/// </summary>
public sealed class Tds194MDeductionCalculator
{
    private static readonly DateOnly EffectiveDate = new(2019, 9, 1);
    private static readonly DateOnly RateChangeDate = new(2024, 10, 1);
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private const decimal Threshold = 5000000m;
    private const decimal RateBeforeChange = 5m;
    private const decimal RateFromChange = 2m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 194M, as amended by Finance (No. 2) Act, 2024 effective 1 October 2024; read with sections 197, 203A and 206AA, as applicable.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 393(1), Table Sl. No. 6(ii), read with section 397 and applicable Income-tax Rules, 2026; Form 141 Schedule C.";

    public TdsDeductionResult Calculate(Tds194MDeductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CreditDate is null && request.PaymentDate is null)
            throw new ArgumentException("At least one of credit date or payment date is required.", nameof(request));
        if (request.CurrentPaymentAmount < 0m || request.PriorAggregatePaidOrCreditedInYear < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Payment values cannot be negative.");

        var date = request.CreditDate is null ? request.PaymentDate!.Value :
            request.PaymentDate is null ? request.CreditDate.Value :
            request.CreditDate.Value <= request.PaymentDate.Value ? request.CreditDate.Value : request.PaymentDate.Value;
        var newAct = date >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = newAct
            ? "Section 393(1), Table Sl. No. 6(ii)"
            : "Section 194M";
        var source = newAct ? NewActSource : OldActSource;

        TdsDeductionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);
        TdsDeductionResult NotApplicable(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, source);

        if (date < EffectiveDate)
            return NotApplicable("Section 194M was not effective for credit/payment events before 1 September 2019. Review the provisions applicable on the transaction date separately.");
        if (!request.PayerIsIndividualOrHuf)
            return NotApplicable("Section 194M applies only to an Individual or HUF payer. Review the applicable TAN-based TDS provision for other payer categories.");
        if (request.PayerRequiredToDeductUnder194C194HOr194J)
            return NotApplicable("The payer is stated to be required to deduct under the specified contractor, commission/brokerage or professional-services provisions; section 194M is not the applicable route.");
        if (!request.PayeeIsResident)
            return Review("The resident-payee calculator cannot determine tax for a non-resident recipient. Refer the payment for section 195 and applicable non-resident analysis.");
        if (!request.ApplicabilityConfirmed)
            return Review("Confirm the payer/payee relationship, nature of payment, statutory classification and that no exclusion or other withholding provision takes priority.");
        if (!request.IsCoveredPaymentCategory)
            return NotApplicable("The payment is not classified as contract work, professional services, or non-insurance commission/brokerage covered by section 194M.");
        if (request.CurrentPaymentAmount == 0m)
            return NotApplicable("No covered payment amount was supplied.");

        var aggregate = request.PriorAggregatePaidOrCreditedInYear + request.CurrentPaymentAmount;
        if (aggregate <= Threshold)
            return NotApplicable($"The payer-payee aggregate of ₹{aggregate:N2} does not exceed the ₹50,00,000 annual threshold.");

        if (!request.PanProvided)
            return Review("PAN is unavailable. Verify the applicable higher-rate provision and any statutory exception before deduction; this calculator deliberately does not guess the rate.");

        var rate = date < RateChangeDate ? RateBeforeChange : RateFromChange;
        if (request.LowerDeductionCertificateRatePercent is decimal certificateRate)
        {
            if (string.IsNullOrWhiteSpace(request.LowerDeductionCertificateReference) ||
                request.CertificateValidFrom is null || request.CertificateValidTo is null ||
                date < request.CertificateValidFrom || date > request.CertificateValidTo ||
                certificateRate < 0m || certificateRate > rate)
                return Review("Lower/nil deduction certificate details are incomplete, invalid, outside their validity period, or exceed the ordinary section rate.");
            rate = certificateRate;
        }

        // The annual aggregate tests the threshold; tax is computed on the current
        // credit/payment event only, avoiding re-taxing earlier payments.
        var taxBase = request.CurrentPaymentAmount;
        var tax = decimal.Round(taxBase * rate / 100m, 2, MidpointRounding.AwayFromZero);
        var reporting = newAct
            ? "Eligible Individual/HUF deductors report this category through Form 141 Schedule C; verify current portal instructions and payment deadlines. Form 141 is not available for other deductor categories."
            : "The section 194M challan-cum-statement route is Form 26QD; the payer is not required to obtain a TAN solely for this section. Verify current portal instructions and payment deadlines.";

        return new(TdsCalculationStatus.Calculated, act, section, rate, taxBase, tax,
            $"{rate}% applies to the current payment/credit of ₹{taxBase:N2} because the payer-payee annual aggregate of ₹{aggregate:N2} exceeds ₹50,00,000. The prior aggregate is used only to test the threshold and is not taxed again. The earlier of credit or payment determines the deduction event. Aggregate all covered payments by this payer to this resident payee for the year, retain contracts/invoices, category and PAN evidence, and confirm no other provision applies. {reporting} This calculator does not prepare or file a statement.",
            source);
    }
}

public sealed record Tds194MDeductionRequest(
    DateOnly? CreditDate,
    DateOnly? PaymentDate,
    decimal CurrentPaymentAmount,
    decimal PriorAggregatePaidOrCreditedInYear,
    bool PayerIsIndividualOrHuf,
    bool PayerRequiredToDeductUnder194C194HOr194J,
    bool PayeeIsResident,
    bool IsCoveredPaymentCategory,
    bool ApplicabilityConfirmed,
    bool PanProvided,
    decimal? LowerDeductionCertificateRatePercent = null,
    string? LowerDeductionCertificateReference = null,
    DateOnly? CertificateValidFrom = null,
    DateOnly? CertificateValidTo = null);
