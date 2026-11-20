namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Calculates TDS on transfer of immovable property other than agricultural land
/// under section 194-IA or section 393(1), Table Sl. No. 3(i).
/// The caller supplies property-level consideration and stamp-duty value, plus the
/// current credit/payment event amount attributable to this deductor-deductee pair.
/// </summary>
public sealed class Tds194IADeductionCalculator
{
    private static readonly DateOnly EffectiveDate = new(2013, 6, 1);
    private static readonly DateOnly JointPartyAggregationDate = new(2024, 10, 1);
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private const decimal Threshold = 5000000m;
    private const decimal RatePercent = 1m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 194-IA, including the joint transferor/transferee aggregation amendment effective 1 October 2024; sections 197 and 206AA, as applicable.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 393(1), Table Sl. No. 3(i) and Note 3; Form 141 Schedule B; section 397 and applicable Income-tax Rules, 2026.";

    public TdsDeductionResult Calculate(Tds194IADeductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CreditDate is null && request.PaymentDate is null)
            throw new ArgumentException("At least one of credit date or payment date is required.", nameof(request));
        if (request.TotalPropertyConsideration < 0m || request.StampDutyValue < 0m ||
            request.CurrentEventConsiderationAmount < 0m ||
            request.ProportionateStampDutyValue < 0m ||
            request.PreviousInstallmentsPaidOrCredited < 0m ||
            request.TotalPaidCreditedIncludingCurrentInstallment < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Property and payment values cannot be negative.");

        var date = request.CreditDate is null ? request.PaymentDate!.Value
            : request.PaymentDate is null ? request.CreditDate.Value
            : request.CreditDate.Value <= request.PaymentDate.Value ? request.CreditDate.Value : request.PaymentDate.Value;
        var newAct = date >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = newAct
            ? "Section 393(1), Table Sl. No. 3(i)"
            : "Section 194-IA";
        var source = newAct ? NewActSource : OldActSource;

        TdsDeductionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);
        TdsDeductionResult NotApplicable(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, source);

        if (date < EffectiveDate)
            return NotApplicable("Section 194-IA was not effective for credit/payment events before 1 June 2013. Review the provisions applicable on the transaction date separately.");
        if (request.IsAgriculturalLand)
            return NotApplicable("Agricultural land is excluded only when it meets the statutory definition, including the specified municipal/cantonment population and aerial-distance tests. Retain the location and classification evidence.");
        if (request.IsCompulsoryAcquisition)
            return NotApplicable("Compulsory acquisition is outside this section 194-IA calculator; refer the transaction for section 194LA or the corresponding applicable provision.");
        if (!request.TransferorIsResident)
            return Review("The seller/transferor is not confirmed as resident. Refer the transaction for section 195 and applicable non-resident analysis.");
        if (!request.ApplicabilityConfirmed)
            return Review("Confirm that this is a transfer of land/building or part of a building, the seller is resident, the payer is the transferee, and no statutory exclusion or different withholding provision applies.");
        if (request.TotalPropertyConsideration == 0m)
            return NotApplicable("No property consideration was supplied.");
        if (request.CurrentEventConsiderationAmount == 0m)
            return NotApplicable("No current credit/payment amount was supplied.");
        if (request.CurrentEventConsiderationAmount > request.TotalPropertyConsideration)
            return Review("The current event amount exceeds the supplied total property consideration. Reconcile installments, incidental transfer charges, co-buyer/co-seller shares and the property-level contract value.");

        var thresholdBase = date < JointPartyAggregationDate
            ? request.TotalPropertyConsideration
            : request.AggregateConsiderationAllTransfereesAllTransferors ?? request.TotalPropertyConsideration;
        if (thresholdBase < 0m)
            return Review("The aggregate consideration across all transferees and transferors cannot be negative.");
        if (date < JointPartyAggregationDate && request.AggregateConsiderationAllTransfereesAllTransferors is not null)
            return Review("The joint transferee/transferor aggregation input is applicable from 1 October 2024. For earlier events, supply the relevant pre-amendment property consideration only.");

        if (newAct)
        {
            if (!request.PropertyLevelAggregationConfirmed)
                return Review("Confirm property-level aggregation of consideration across all transferors and transferees, including the allocation for this buyer/seller event.");
            if (thresholdBase < Threshold)
                return NotApplicable($"The aggregate consideration for the property of ₹{thresholdBase:N2} is below ₹50,00,000.");
        }
        else
        {
            if (thresholdBase < Threshold && request.StampDutyValue < Threshold)
                return NotApplicable($"Both the property consideration (₹{thresholdBase:N2}) and stamp-duty value (₹{request.StampDutyValue:N2}) are below ₹50,00,000.");
        }

        if (!request.PanProvided)
            return Review("The seller's PAN is unavailable. Verify the applicable higher-rate provision under section 206AA or section 397 and any statutory exception before deduction; this calculator deliberately does not guess the rate.");

        var taxBase = request.CurrentEventConsiderationAmount;
        if (newAct)
        {
            if (request.IsLastInstallmentOrLumpSum is null)
                return Review("Select whether this is a first/subsequent instalment or the last instalment/lump-sum transaction, as required by Form 141 Schedule B.");

            if (request.IsLastInstallmentOrLumpSum.Value)
            {
                if (request.ProportionateStampDutyValue is null ||
                    request.PreviousInstallmentsPaidOrCredited is null ||
                    request.TotalPaidCreditedIncludingCurrentInstallment is null ||
                    request.TotalPaidCreditedIncludingCurrentInstallment < request.PreviousInstallmentsPaidOrCredited)
                    return Review("For a last instalment or lump-sum transaction, provide the buyer/seller-share-adjusted stamp-duty value, previous instalments and total consideration paid/credited including the current instalment.");

                var currentEventBase = request.TotalPaidCreditedIncludingCurrentInstallment.Value -
                    request.PreviousInstallmentsPaidOrCredited.Value;
                taxBase = Math.Max(request.ProportionateStampDutyValue.Value, currentEventBase);
            }
            // Form 141 Schedule B uses the present transaction amount for
            // first/subsequent instalments; the proportionate stamp-duty
            // value is used only for last-instalment or lump-sum entries.
        }

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

        var tax = decimal.Round(taxBase * rate / 100m, 2, MidpointRounding.AwayFromZero);
        var thresholdNote = date < JointPartyAggregationDate
            ? "Before 1 October 2024, the ₹50 lakh test is applied to the consideration for the property and stamp-duty value; both must be below the limit for non-deduction."
            : "From 1 October 2024, apply the ₹50 lakh test to the aggregate consideration paid/payable by all transferees to all transferors for the property.";
        var baseNote = newAct
            ? "Under Form 141 Schedule B, first/subsequent instalments use the present transaction amount. For a last instalment or lump-sum transaction, the tax base is the higher of the buyer/seller-share-adjusted stamp-duty value and the current event amount derived from cumulative paid/credited consideration less previous instalments."
            : "Under section 194-IA, the 1% rate is applied to the consideration for the current credit/payment event; stamp-duty value is used for the threshold test.";
        var reporting = newAct
            ? "For eligible PAN-based deductors, the challan-cum-statement route is Form 141 Schedule B."
            : "The PAN-based challan-cum-statement route is Form 26QB; no TAN is required solely for section 194-IA.";

        return new(TdsCalculationStatus.Calculated, act, section, rate, taxBase, tax,
            $"{rate}% applies to the current event tax base of ₹{taxBase:N2}. {thresholdNote} {baseNote} Deduct at the earlier of credit or payment. Include incidental charges connected with the transfer in the consideration; verify agricultural-land status using the statutory location tests. For multiple buyers/sellers, allocate each deductor's event share and ensure the property-level threshold is tested only once against the complete transaction. {reporting} This calculator does not prepare or file a statement.",
            source);
    }
}

public sealed record Tds194IADeductionRequest(
    DateOnly? CreditDate,
    DateOnly? PaymentDate,
    decimal TotalPropertyConsideration,
    decimal StampDutyValue,
    decimal CurrentEventConsiderationAmount,
    bool TransferorIsResident,
    bool IsAgriculturalLand,
    bool IsCompulsoryAcquisition,
    bool ApplicabilityConfirmed,
    bool PanProvided,
    bool PropertyLevelAggregationConfirmed,
    decimal? AggregateConsiderationAllTransfereesAllTransferors = null,
    decimal? LowerDeductionCertificateRatePercent = null,
    string? LowerDeductionCertificateReference = null,
    DateOnly? CertificateValidFrom = null,
    DateOnly? CertificateValidTo = null,
    bool? IsLastInstallmentOrLumpSum = null,
    decimal? ProportionateStampDutyValue = null,
    decimal? PreviousInstallmentsPaidOrCredited = null,
    decimal? TotalPaidCreditedIncludingCurrentInstallment = null);
