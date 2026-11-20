namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Historical-only validation of TCS under section 206C(1H).
/// This provision applied from 1 October 2020 through 31 March 2025 and
/// is not applicable to receipts on or after 1 April 2025.
/// The validator is a workpaper aid; it does not file statements or remit tax.
/// </summary>
public sealed class Tcs206C1HHistoricalValidator
{
    private static readonly DateOnly EffectiveFrom = new(2020, 10, 1);
    private static readonly DateOnly WithdrawnFrom = new(2025, 4, 1);
    private const decimal BuyerReceiptThreshold = 5000000m;
    private const decimal NormalRatePercent = 0.1m;
    private const decimal NoPanRatePercent = 1m;

    private const string Source =
        "Income-tax Act, 1961 section 206C(1H), read with section 206CC and section 206C(9); Finance Act, 2025 section 72; CBDT Circular No. 17/2020 dated 29 September 2020.";

    public TcsCollectionResult Validate(Tcs206C1HHistoricalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ReceiptDate is null)
            throw new ArgumentException("A receipt date is required; section 206C(1H) was triggered on receipt, not on debit.", nameof(request));
        if (request.CurrentAmountReceived < 0m || request.PriorAggregateReceiptsInFinancialYear < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Receipt amounts cannot be negative.");
        if (request.LowerRateCertificateRatePercent is < 0m or > 100m)
            throw new ArgumentOutOfRangeException(nameof(request), "The lower-rate certificate percentage must be between 0 and 100.");

        var date = request.ReceiptDate.Value;
        const string act = "Income-tax Act, 1961";
        const string section = "Section 206C(1H) (historical only)";

        TcsCollectionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, Source);
        TcsCollectionResult NotApplicable(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, Source);

        if (date >= WithdrawnFrom)
            return NotApplicable("Section 206C(1H) is not applicable to receipts on or after 1 April 2025. Do not calculate or collect TCS under this subsection for this receipt. For current sale-of-goods transactions, assess the applicable provisions such as section 194Q and other relevant TCS sections separately.");
        if (date < EffectiveFrom)
            return NotApplicable("Section 206C(1H) is not applicable because it became effective only from 1 October 2020; this receipt predates its commencement.");

        if (!request.SellerEligibilityConfirmed || !request.SellerPrecedingYearTurnoverExceedsTenCrore)
            return Review("Confirm the seller's total sales, gross receipts or turnover from business exceeded ₹10 crore in the immediately preceding financial year, and retain the financial statements/turnover evidence. Do not infer eligibility from the current-year turnover.");
        if (!request.BuyerEligibilityChecked)
            return Review("Verify the buyer definition and retain evidence. Exclusions include Central/State Government, specified diplomatic or foreign representations, local authorities, a person importing goods into India and other notified persons.");
        if (request.BuyerIsStatutorilyExcluded)
            return NotApplicable("The buyer has been confirmed as falling within a statutory exclusion from section 206C(1H). Retain the exact exclusion category and supporting evidence.");
        if (request.GoodsExportedFromIndia)
            return NotApplicable("Goods exported out of India are outside section 206C(1H). Retain export and shipping evidence.");
        if (request.GoodsCoveredBySection206C1Or1FOr1G)
            return NotApplicable("Goods covered by section 206C(1), 206C(1F) or 206C(1G) are excluded from section 206C(1H). Verify the other subsection's applicability and avoid duplicate collection.");
        if (request.BuyerLiableUnderAnotherTdsProvision && request.BuyerHasDeductedThatTax)
            return NotApplicable("The buyer was liable to deduct tax under another provision on these goods and has deducted that tax. The second proviso to section 206C(1H) therefore disapplies TCS; retain buyer TDS evidence.");
        if (request.BuyerLiableUnderAnotherTdsProvision && !request.BuyerHasDeductedThatTax)
            return Review("The buyer may be liable to deduct TDS under another provision (commonly section 194Q). The statutory no-TCS exception requires that the buyer has actually deducted that tax. Verify the buyer's deduction status and evidence before concluding.");
        if (request.CurrentAmountReceived == 0m)
            return NotApplicable("No current receipt amount was supplied.");

        // CBDT Circular 17/2020 clarifies that the ₹50 lakh threshold is tested
        // using aggregate receipts from the buyer in the financial year. TCS is
        // collected only on the portion of the current receipt that takes the
        // aggregate receipts above ₹50 lakh.
        var prior = request.PriorAggregateReceiptsInFinancialYear;
        var cumulative = prior + request.CurrentAmountReceived;
        var taxableAmount = Math.Max(0m, cumulative - BuyerReceiptThreshold)
                            - Math.Max(0m, prior - BuyerReceiptThreshold);
        if (taxableAmount <= 0m)
            return NotApplicable($"Aggregate receipts from this buyer for the financial year are ₹{cumulative:N2}, which do not exceed the ₹50 lakh threshold. Carry forward the cumulative buyer-wise receipts for subsequent receipts in the same financial year.");

        var rate = request.PanProvided ? NormalRatePercent : NoPanRatePercent;
        if (request.LowerRateCertificatePresented)
        {
            if (!request.PanProvided)
                return Review("A lower/nil collection certificate cannot be applied without valid PAN. The section 206C(1H) PAN-default rate is 1%; verify PAN and any certificate before posting.");
            if (!request.LowerRateCertificateValid || request.LowerRateCertificateRatePercent is null)
                return Review("A lower/nil TCS certificate has been presented but its validity, scope, rate and effective dates have not been confirmed. Verify the certificate under section 206C(9) before applying it.");
            rate = request.LowerRateCertificateRatePercent.Value;
        }

        var tax = decimal.Round(taxableAmount * rate / 100m, 2, MidpointRounding.AwayFromZero);
        var panNote = request.PanProvided
            ? "Valid PAN has been confirmed."
            : "PAN was not furnished; the specific section 206C(1H) proviso modifies the section 206CC rate to 1%.";
        var certificateNote = request.LowerRateCertificatePresented
            ? $" A verified lower-rate certificate has been applied at {rate:N2}%."
            : string.Empty;

        return new(TdsCalculationStatus.Calculated, act, section, rate,
            taxableAmount, tax,
            $"Historical TCS under section 206C(1H) applies to the portion of this receipt exceeding the ₹50 lakh buyer-wise aggregate receipt threshold. Prior financial-year receipts: ₹{prior:N2}; current receipt: ₹{request.CurrentAmountReceived:N2}; cumulative receipts: ₹{cumulative:N2}; taxable portion of current receipt: ₹{taxableAmount:N2}. TCS rate: {rate:N2}%; amount: ₹{tax:N2}. Collection was triggered on receipt of sale consideration (including advance), not merely on invoice/debit. {panNote} Retain buyer-wise receipt ledger, preceding-year seller turnover evidence, buyer eligibility/exclusion checks, goods classification, export status, TDS evidence and PAN/certificate evidence.{certificateNote}",
            Source);
    }
}

/// <summary>
/// Inputs for historical section 206C(1H) validation. Prior receipts must be
/// the aggregate receipts from the same buyer in the same financial year.
/// </summary>
public sealed record Tcs206C1HHistoricalRequest(
    DateOnly? ReceiptDate,
    decimal PriorAggregateReceiptsInFinancialYear,
    decimal CurrentAmountReceived,
    bool SellerEligibilityConfirmed,
    bool SellerPrecedingYearTurnoverExceedsTenCrore,
    bool BuyerEligibilityChecked,
    bool BuyerIsStatutorilyExcluded,
    bool GoodsExportedFromIndia,
    bool GoodsCoveredBySection206C1Or1FOr1G,
    bool BuyerLiableUnderAnotherTdsProvision,
    bool BuyerHasDeductedThatTax,
    bool PanProvided,
    bool LowerRateCertificatePresented = false,
    bool LowerRateCertificateValid = false,
    decimal? LowerRateCertificateRatePercent = null);
