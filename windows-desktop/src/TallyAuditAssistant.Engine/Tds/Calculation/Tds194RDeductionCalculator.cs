namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Calculates resident-payee TDS on business/profession benefits and perquisites
/// under section 194R or section 393(1), Table Sl. No. 8(iv).
/// The caller supplies a defensible value excluding GST and confirms applicability
/// and CBDT-guidance exclusions. This calculator does not determine fair market value.
/// </summary>
public sealed class Tds194RDeductionCalculator
{
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private const decimal Threshold = 20000m;
    private const decimal RatePercent = 10m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 194R(1), (2) and (4), section 206AA and section 197, as applicable; CBDT Circulars 12/2022 and 18/2022.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 393(1), Table Sl. No. 8(iv), read with section 397(2), as applicable; corresponding prescribed rules and CBDT guidance.";

    public TdsDeductionResult Calculate(Tds194RDeductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CreditOrProvisionDate is null)
            throw new ArgumentException("The date on which the benefit/perquisite is provided or made available is required.", nameof(request));
        if (request.CurrentBenefitValue < 0m || request.PriorAggregateBenefitValueInYear < 0m ||
            request.CashAvailableForTds < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Benefit values and cash available for TDS cannot be negative.");

        var date = request.CreditOrProvisionDate.Value;
        var newAct = date >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = newAct ? "Section 393(1), Table Sl. No. 8(iv)" : "Section 194R";
        var source = newAct ? NewActSource : OldActSource;

        TdsDeductionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);
        TdsDeductionResult NotApplicable(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, source);

        if (!request.ApplicabilityConfirmed)
            return Review("Confirm that the recipient is resident and the benefit/perquisite arises from the recipient's business or exercise of a profession. Confirm the provider/recipient relationship and applicable CBDT guidance before calculation.");
        if (!request.RecipientIsResident)
            return NotApplicable("This calculator covers resident recipients only. Review the applicable non-resident withholding provisions and treaty separately.");
        if (!request.BenefitArisesFromBusinessOrProfession)
            return NotApplicable("The supplied facts do not establish that the benefit/perquisite arises from the recipient's business or profession.");
        if (request.IsExcludedUnderCbdtGuidance)
            return NotApplicable("The caller has identified an exclusion/relaxation under applicable CBDT guidance (for example, qualifying discounts/rebates, buy-more-get-more schemes, qualifying business conferences, or qualifying reimbursements). Retain the supporting facts and circular reference.");
        if (!request.ValueExcludesGst)
            return Review("Supply the benefit/perquisite value excluding GST, as required by CBDT guidance. Do not silently deduct an assumed GST component.");
        if (request.CurrentBenefitValue == 0m)
            return NotApplicable("No benefit/perquisite value was supplied.");

        if (request.ProviderIsIndividualOrHuf)
        {
            if (!request.ProviderTurnoverTestConfirmed)
                return Review("For an individual/HUF provider, confirm preceding-year business turnover above ₹1 crore or professional gross receipts above ₹50 lakh.");
            if (!request.ProviderIsWithinTurnoverThreshold)
                return NotApplicable("The individual/HUF provider is below the preceding-year business/profession turnover thresholds and is not required to deduct under this provision.");
        }

        var aggregate = request.PriorAggregateBenefitValueInYear + request.CurrentBenefitValue;
        if (aggregate <= Threshold)
            return NotApplicable($"The recipient-year aggregate benefit/perquisite value of ₹{aggregate:N2} does not exceed the ₹20,000 threshold.");

        if (!request.PanProvided)
            return Review("PAN is unavailable. The higher-rate provision may apply; verify the applicable section 206AA/section 397 rule and any exception before deduction. This calculator deliberately does not guess the rate.");

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
        var isInKind = request.EntirelyInKind || request.CashAvailableForTds < tax;
        if (isInKind && !request.TaxPaymentBeforeReleaseConfirmed)
            return Review("The benefit is wholly/partly in kind or available cash is insufficient to cover TDS. Before releasing it, obtain confirmation that the recipient has paid the required tax as advance tax (with challan evidence), or otherwise ensure tax is paid. Record the challan for the applicable TDS statement.");
        var kindNote = isInKind
            ? "The benefit is wholly/partly in kind or cash is insufficient; the caller confirms tax was paid before release and supporting challan evidence is retained."
            : "Available cash is sufficient to meet the TDS liability before release.";
        return new(TdsCalculationStatus.Calculated, act, section, rate, aggregate, tax,
            $"{rate}% applies to the full recipient-year aggregate value of ₹{aggregate:N2} once it exceeds ₹20,000; it is not limited to the excess over the threshold. Value must be determined using the applicable fair-market-value/purchase-cost/manufacturer-selling-price rule and excludes GST. {kindNote} The caller must retain valuation and applicability evidence.",
            source);
    }
}

public sealed record Tds194RDeductionRequest(
    DateOnly? CreditOrProvisionDate,
    decimal CurrentBenefitValue,
    decimal PriorAggregateBenefitValueInYear,
    bool RecipientIsResident,
    bool BenefitArisesFromBusinessOrProfession,
    bool ApplicabilityConfirmed,
    bool PanProvided,
    bool ValueExcludesGst,
    bool EntirelyInKind = false,
    decimal CashAvailableForTds = 0m,
    bool TaxPaymentBeforeReleaseConfirmed = false,
    bool IsExcludedUnderCbdtGuidance = false,
    bool ProviderIsIndividualOrHuf = false,
    bool ProviderTurnoverTestConfirmed = true,
    bool ProviderIsWithinTurnoverThreshold = true,
    decimal? LowerDeductionCertificateRatePercent = null,
    string? LowerDeductionCertificateReference = null,
    DateOnly? CertificateValidFrom = null,
    DateOnly? CertificateValidTo = null);
