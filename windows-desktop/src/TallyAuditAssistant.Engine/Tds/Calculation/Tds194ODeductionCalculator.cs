namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Calculates basic resident e-commerce participant withholding under section
/// 194-O and section 393(1), Table Sl. No. 8(v). The caller must aggregate all
/// sales/services facilitated for the participant by the operator for the
/// relevant year, including direct buyer-to-participant payments, without
/// double counting amounts settled through the operator.
/// </summary>
public sealed class Tds194ODeductionCalculator
{
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private const decimal IndividualHufAnnualExemption = 500000m;
    private const decimal RatePercent = 0.1m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 194-O, as amended with effect from 1 October 2024; CBDT Circular No. 20/2023 and applicable notifications.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 393(1), Table Sl. No. 8(v), read with section 393(4), Table Sl. No. 11 and Notes 3–4.";

    public TdsDeductionResult Calculate(Tds194ODeductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CreditDate is null && request.PaymentDate is null)
            throw new ArgumentException("At least one of credit date or payment date is required.", nameof(request));
        if (request.GrossFacilitatedSalesOrServicesAmount < 0 ||
            request.PriorAggregateGrossFacilitatedAmountInYear < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Current and prior gross facilitated amounts cannot be negative.");

        var triggerDate = request.CreditDate is null ? request.PaymentDate!.Value
            : request.PaymentDate is null ? request.CreditDate.Value
            : request.CreditDate.Value <= request.PaymentDate.Value ? request.CreditDate.Value : request.PaymentDate.Value;
        var newAct = triggerDate >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = newAct ? "Section 393(1), Table Sl. No. 8(v)" : "Section 194-O";
        var source = newAct ? NewActSource : OldActSource;

        TdsDeductionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);

        TdsDeductionResult NotApplicable(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, source);

        if (!request.ApplicabilityConfirmed)
            return Review("Confirm that the participant is resident in India, the operator owns/operates/manages the electronic commerce facility, and the sale or service is facilitated through that platform. Review multi-operator/ONDC arrangements before assigning the deductor.");
        if (!request.ParticipantIsResident)
            return NotApplicable("The participant is not resident in India; this resident-payee calculator does not determine the applicable non-resident withholding.");
        if (request.IsAdvertisementOrUnrelatedPlatformService)
            return NotApplicable("Amounts for hosting advertisements or platform services unrelated to the facilitated sale/service are outside the section 194-O/section 393(1) Table 8(v) carve-in.");
        if (request.IsVirtualDigitalAssetTransaction)
            return NotApplicable("Where the transaction is also covered by section 393(1), Table Sl. No. 8(vi) for a virtual digital asset, the new Act's specific priority note assigns deduction to that provision rather than Table 8(v). Review the applicable VDA withholding separately.");
        if (request.GrossFacilitatedSalesOrServicesAmount == 0m)
            return NotApplicable("No gross facilitated sale or service amount was supplied.");

        var cumulativeGross = request.PriorAggregateGrossFacilitatedAmountInYear +
            request.GrossFacilitatedSalesOrServicesAmount;
        var qualifyingIndividualOrHuf = request.ParticipantEntityType == TdsDeducteeEntityType.IndividualOrHuf;
        if (qualifyingIndividualOrHuf && request.PanOrAadhaarProvided &&
            cumulativeGross <= IndividualHufAnnualExemption)
            return NotApplicable("The participant is an individual/HUF, PAN or Aadhaar has been furnished, and the gross facilitated sales/services for the year do not exceed ₹5 lakh.");

        if (!request.PanOrAadhaarProvided)
            return Review("PAN/Aadhaar details have not been confirmed. The ₹5 lakh individual/HUF exemption is unavailable without PAN or Aadhaar; verify identity reporting and any applicable higher-rate provisions before deduction.");

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

        var tax = decimal.Round(request.GrossFacilitatedSalesOrServicesAmount * rate / 100m,
            2, MidpointRounding.AwayFromZero);
        var paymentNote = request.IncludesDirectBuyerPayments
            ? "The supplied gross amount includes buyer-to-participant direct payments deemed to be operator payments; do not add those payments again."
            : "Confirm that the gross amount includes any buyer-to-participant direct payments deemed to be operator payments.";
        var thresholdNote = qualifyingIndividualOrHuf
            ? "The individual/HUF ₹5 lakh annual exemption is not available because the aggregate exceeds the limit or PAN/Aadhaar was not furnished."
            : "No ₹5 lakh annual exemption is available to a participant other than an individual/HUF.";
        return new(TdsCalculationStatus.Calculated, act, section, rate,
            request.GrossFacilitatedSalesOrServicesAmount, tax,
            $"{rate}% applied to the full current gross facilitated sale/service amount (not merely the amount exceeding ₹5 lakh). {thresholdNote} {paymentNote} The operator deducts at the earlier of credit or payment; gross sales/services include facilitated transactions even where the buyer pays the participant directly.",
            source);
    }
}

public sealed record Tds194ODeductionRequest(
    DateOnly? CreditDate,
    DateOnly? PaymentDate,
    decimal GrossFacilitatedSalesOrServicesAmount,
    decimal PriorAggregateGrossFacilitatedAmountInYear,
    TdsDeducteeEntityType ParticipantEntityType,
    bool ParticipantIsResident,
    bool PanOrAadhaarProvided,
    bool ApplicabilityConfirmed,
    bool IncludesDirectBuyerPayments,
    bool IsAdvertisementOrUnrelatedPlatformService = false,
    bool IsVirtualDigitalAssetTransaction = false,
    decimal? LowerDeductionCertificateRatePercent = null,
    string? LowerDeductionCertificateReference = null,
    DateOnly? CertificateValidFrom = null,
    DateOnly? CertificateValidTo = null);
