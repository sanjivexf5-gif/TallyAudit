namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Calculates TCS on sale of a motor vehicle or notified high-value goods
/// under section 206C(1F) or section 394(1), Table Sl. No. 6.
/// The caller must identify the individual item and confirm seller/buyer eligibility.
/// </summary>
public sealed class Tcs206C1FDeductionCalculator
{
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private static readonly DateOnly NotifiedGoodsEffectiveDate = new(2025, 4, 22);
    private const decimal ItemValueThreshold = 1000000m;
    private const decimal RatePercent = 1m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 206C(1F), as amended by Finance (No. 2) Act, 2024; CBDT Notification No. 36/2025 dated 22 April 2025 (S.O. 1825(E)); section 206CC for PAN default.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 394(1), Table Sl. No. 6, read with section 397 and the Income-tax Rules, 2026.";

    public TcsCollectionResult Calculate(Tcs206C1FCollectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ReceiptDate is null && request.AmountDebitDate is null)
            throw new ArgumentException("At least one receipt date or amount-debit date is required.", nameof(request));
        if (request.ItemSaleValue < 0m || request.CurrentAmountReceivedOrDebited < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Item value and current receipt/debit amount cannot be negative.");

        var triggerDate = request.ReceiptDate is null ? request.AmountDebitDate!.Value
            : request.AmountDebitDate is null ? request.ReceiptDate.Value
            : request.ReceiptDate.Value <= request.AmountDebitDate.Value ? request.ReceiptDate.Value : request.AmountDebitDate.Value;
        var newAct = triggerDate >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = newAct ? "Section 394(1), Table Sl. No. 6" : "Section 206C(1F)";
        var source = newAct ? NewActSource : OldActSource;

        TcsCollectionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);
        TcsCollectionResult NotApplicable(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, source);

        if (!request.SellerEligibilityConfirmed)
            return Review("Confirm that the seller falls within the statutory meaning of seller/person responsible for collection, including the applicable individual/HUF preceding-year turnover test. Do not infer seller eligibility from a sales ledger alone.");
        if (!request.ItemCategoryConfirmed)
            return Review("Confirm that the item is a motor vehicle or one of the goods specifically notified under section 206C(1F). The notified categories include wristwatches, art pieces, collectibles, yachts/boats/canoes/helicopters, sunglasses, handbags/purses, shoes, sportswear/equipment, home theatre systems, and horses for racing/polo.");
        if (!request.BuyerExemptionChecked)
            return Review("Check buyer exclusions and retain the exact statutory category/evidence before collecting TCS. Relevant exclusions include specified Government/embassy/local-authority buyers, passenger-transport PSUs, RBI and motor-vehicle dealers/distributors as applicable.");
        if (request.BuyerIsExempt)
            return NotApplicable("The buyer has been confirmed as falling within a statutory exclusion from section 206C(1F)/section 394(1), Table Sl. No. 6. Retain the supporting exemption evidence.");
        if (triggerDate < NotifiedGoodsEffectiveDate && !request.IsMotorVehicle)
            return NotApplicable("The notified-goods extension became effective on 22 April 2025. Before that date, this provision applied to motor vehicles only.");
        if (request.ItemSaleValue <= ItemValueThreshold)
            return NotApplicable($"The value of this individual item is ₹{request.ItemSaleValue:N2}, which does not exceed ₹{ItemValueThreshold:N0}. The test is per item, not annual aggregate or invoice total across unrelated items.");
        if (request.CurrentAmountReceivedOrDebited == 0m)
            return NotApplicable("No current receipt/debit event amount was supplied.");
        if (!request.PanProvided)
            return Review("Buyer PAN is unavailable or invalid. The higher TCS rate under section 206CC/section 397 may apply (for the old Act, higher of twice the normal rate or 5%, subject to the statutory cap). Verify the applicable new-Act rule and any exception before posting; this calculator does not silently assume PAN compliance.");
        if (request.LowerRateCertificatePresented)
            return Review("A lower/nil TCS certificate is not available for section 206C(1F). Do not apply the certificate to this transaction; verify the facts and applicable law.");

        var tax = decimal.Round(request.CurrentAmountReceivedOrDebited * RatePercent / 100m, 2, MidpointRounding.AwayFromZero);
        var timing = newAct
            ? "Under the new Act, collection is triggered at the earlier of debit of the amount payable to the buyer's account or receipt of that amount."
            : "Under the old Act, collection is triggered at the time the seller receives the amount as consideration, including advance receipts.";
        return new(TdsCalculationStatus.Calculated, act, section, RatePercent,
            request.CurrentAmountReceivedOrDebited, tax,
            $"The individual item value of ₹{request.ItemSaleValue:N2} exceeds ₹{ItemValueThreshold:N0}. TCS at 1% applies to the entire sale consideration, not merely the excess over ₹10 lakh. This event's amount is ₹{request.CurrentAmountReceivedOrDebited:N2}; collect ₹{tax:N2} on this event and apply the same item-level threshold to subsequent instalments without taxing prior receipts again. {timing} Do not apply a lower-rate certificate. Retain item identification, category, sale value, receipt/debit schedule, seller eligibility, buyer exemption and PAN evidence.",
            source);
    }
}

/// <summary>
/// TCS workpaper result. The common calculation status enum is reused to keep
/// the pure calculation layer consistent; TcsAmount is the amount collectible.
/// </summary>
public sealed record TcsCollectionResult(
    TdsCalculationStatus Status,
    string GoverningAct,
    string SectionReference,
    decimal? AppliedRatePercent,
    decimal TaxableAmount,
    decimal TcsAmount,
    string Explanation,
    string SourceReference);

public sealed record Tcs206C1FCollectionRequest(
    DateOnly? ReceiptDate,
    DateOnly? AmountDebitDate,
    decimal ItemSaleValue,
    decimal CurrentAmountReceivedOrDebited,
    bool IsMotorVehicle,
    bool ItemCategoryConfirmed,
    bool SellerEligibilityConfirmed,
    bool BuyerExemptionChecked,
    bool BuyerIsExempt,
    bool PanProvided,
    bool LowerRateCertificatePresented = false);
