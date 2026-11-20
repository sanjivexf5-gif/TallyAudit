namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Calculates TDS on consideration for transfer of a virtual digital asset
/// under section 194S / section 393(1), Table Sl. No. 8(vi).
/// The caller supplies the complete payer-payee aggregate for the tax year.
/// </summary>
public sealed class Tds194SDeductionCalculator
{
    private static readonly DateOnly EffectiveDate = new(2022, 7, 1);
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private const decimal RatePercent = 1m;
    private const decimal SpecifiedPersonThreshold = 50000m;
    private const decimal OtherPayerThreshold = 10000m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 194S, read with sections 206AA and 197, as applicable; CBDT Circular No. 13/2022 and applicable VDA guidance.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 393(1), Table Sl. No. 8(vi), section 393(4) Table Sl. No. 12.C(a), Note 6 and section 397, as applicable; Income-tax Rules, 2026.";

    public TdsDeductionResult Calculate(Tds194SDeductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CreditOrPaymentDate is null)
            throw new ArgumentException("The earlier credit/payment date is required.", nameof(request));
        if (request.CurrentConsiderationAmount < 0m || request.PriorAggregateConsiderationInYear < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Consideration values cannot be negative.");
        if (request.CashAvailableForDeduction < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Cash available for deduction cannot be negative.");

        var date = request.CreditOrPaymentDate.Value;
        var newAct = date >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = newAct
            ? "Section 393(1), Table Sl. No. 8(vi)"
            : "Section 194S";
        var source = newAct ? NewActSource : OldActSource;

        TdsDeductionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);
        TdsDeductionResult NotApplicable(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, source);

        if (date < EffectiveDate)
            return NotApplicable("Section 194S was not effective for credit/payment events before 1 July 2022. Review the provisions applicable on the transaction date separately.");

        if (!request.IsTransferOfVdaConfirmed)
            return Review("Confirm that the asset transferred meets the statutory definition of a virtual digital asset. Do not infer VDA classification from a wallet, token or platform label alone.");
        if (!request.PayeeIsResident)
            return Review("The resident-payee calculator cannot determine tax for a non-resident transferor. Refer the transaction for section 195 / applicable non-resident analysis.");
        if (!request.ApplicabilityConfirmed)
            return Review("Confirm that the supplied consideration is payable by this payer to this transferor for transfer of the VDA and that no statutory exclusion or different responsible-person arrangement applies.");
        if (request.TaxAlreadyDeductedOnTransaction)
            return NotApplicable("TDS has already been deducted by the responsible payer on this transaction. Do not deduct again; retain the payer's undertaking and deduction evidence.");
        if (request.ExchangeHasUndertakenDeduction)
            return Review("The exchange has been identified as having undertaken the alternative deduction/deposit mechanism. Reconcile the exchange's deduction and reporting evidence; do not deduct again from the same transaction.");
        if (request.CurrentConsiderationAmount == 0m)
            return NotApplicable("No VDA consideration amount was supplied.");

        var specifiedPerson = false;
        if (request.PayerIsIndividualOrHuf)
        {
            if (!request.SpecifiedPersonStatusConfirmed)
                return Review("Confirm the payer's specified-person status using the immediately preceding year's business/profession turnover or confirmation that the payer has no income under the head Profits and gains of business or profession.");
            specifiedPerson = request.PayerMeetsSpecifiedPersonTest;
        }

        var threshold = specifiedPerson ? SpecifiedPersonThreshold : OtherPayerThreshold;
        var aggregate = request.PriorAggregateConsiderationInYear + request.CurrentConsiderationAmount;
        if (aggregate <= threshold)
            return NotApplicable($"The payer-payee aggregate VDA consideration of ₹{aggregate:N2} does not exceed the applicable annual threshold of ₹{threshold:N2}.");

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

        // The annual aggregate only tests the threshold. Once crossed, deduct on
        // the consideration for the current credit/payment event, not prior events.
        var taxBase = request.CurrentConsiderationAmount;
        var tax = decimal.Round(taxBase * rate / 100m, 0, MidpointRounding.AwayFromZero);
        var inKind = request.ConsiderationWhollyOrPartlyInKind;
        if (inKind && request.CashAvailableForDeduction < tax && !request.TaxPaidBeforeReleaseConfirmed)
            return Review("Consideration is wholly/partly in kind and available cash is insufficient to meet TDS on the full consideration. Confirm tax payment before releasing the VDA/consideration and retain evidence, as required by the applicable in-kind rule.");

        var form = newAct && request.PayerIsIndividualOrHuf
            ? "For an eligible resident Individual/HUF deductor, the new-Act challan-cum-statement route is Form 141 Schedule D; confirm deductee and transaction details against the portal requirements."
            : newAct
                ? "For a payer other than an Individual/HUF, use the applicable TAN-based reporting route; Form 141 Schedule D is not the reporting route for this payer category."
                : "For an eligible specified person, verify the PAN-based Form 26QE process; other deductors use the applicable TAN-based reporting route.";

        return new(TdsCalculationStatus.Calculated, act, section, rate, taxBase, tax,
            $"{rate}% applies to the current consideration of ₹{taxBase:N2} because the payer-payee annual aggregate of ₹{aggregate:N2} exceeds the applicable ₹{threshold:N2} threshold. The prior-year-to-date consideration is used to test the threshold but is not taxed again. Include cash, in-kind and VDA-exchange consideration at a supportable value, exclude GST/charges levied by the deductor where the CBDT guidance applies, and use the earlier credit/payment event. {form} The caller must aggregate all transfers for this payer-payee pair in the tax year and retain valuation, classification, PAN and deduction evidence. This calculator does not prepare or file a statement.",
            source);
    }
}

public sealed record Tds194SDeductionRequest(
    DateOnly? CreditOrPaymentDate,
    decimal CurrentConsiderationAmount,
    decimal PriorAggregateConsiderationInYear,
    bool IsTransferOfVdaConfirmed,
    bool PayeeIsResident,
    bool ApplicabilityConfirmed,
    bool PayerIsIndividualOrHuf,
    bool SpecifiedPersonStatusConfirmed,
    bool PayerMeetsSpecifiedPersonTest,
    bool PanProvided,
    bool ConsiderationWhollyOrPartlyInKind,
    decimal CashAvailableForDeduction,
    bool TaxPaidBeforeReleaseConfirmed,
    bool ExchangeHasUndertakenDeduction = false,
    bool TaxAlreadyDeductedOnTransaction = false,
    decimal? LowerDeductionCertificateRatePercent = null,
    string? LowerDeductionCertificateReference = null,
    DateOnly? CertificateValidFrom = null,
    DateOnly? CertificateValidTo = null);
