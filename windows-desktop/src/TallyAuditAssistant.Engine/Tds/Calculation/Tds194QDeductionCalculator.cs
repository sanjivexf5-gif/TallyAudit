namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Calculates basic resident-seller withholding on purchases of goods under
/// section 194Q and section 393(1), Table Sl. No. 8(ii).
///
/// The caller must supply seller-specific prior financial-year aggregate and
/// confirm buyer/seller eligibility, goods classification and transaction
/// exclusions. This calculator intentionally returns ReviewRequired for
/// unresolved competing TDS/TCS provisions or missing PAN; it does not guess
/// priority or a higher rate.
/// </summary>
public sealed class Tds194QDeductionCalculator
{
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private const decimal SellerThreshold = 5000000m;
    private const decimal BuyerTurnoverThreshold = 100000000m;
    private const decimal RatePercent = 0.1m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 194Q, as amended; CBDT Circular No. 13 of 2021 and applicable notifications.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 393(1), Table Sl. No. 8(ii), Note 1, read with section 402(6) and applicable notifications.";

    public TdsDeductionResult Calculate(Tds194QDeductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CreditDate is null && request.PaymentDate is null)
            throw new ArgumentException("At least one of credit date or payment date is required.", nameof(request));
        if (request.GrossPurchaseAmount < 0 || request.SeparatelyStatedGstAmount < 0 ||
            request.SeparatelyStatedGstAmount > request.GrossPurchaseAmount ||
            request.PriorAggregatePurchasesFromSellerInFinancialYear < 0 ||
            request.BuyerBusinessTurnoverInPrecedingYear < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Purchase, GST, prior aggregate and turnover amounts must be non-negative and GST cannot exceed the gross purchase amount.");

        var triggerDate = request.CreditDate is null ? request.PaymentDate!.Value
            : request.PaymentDate is null ? request.CreditDate.Value
            : request.CreditDate.Value <= request.PaymentDate.Value ? request.CreditDate.Value : request.PaymentDate.Value;
        var newAct = triggerDate >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = newAct ? "Section 393(1), Table Sl. No. 8(ii)" : "Section 194Q";
        var source = newAct ? NewActSource : OldActSource;

        TdsDeductionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);

        TdsDeductionResult NotApplicable(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, source);

        if (!request.ApplicabilityConfirmed)
            return Review("Purchase TDS applicability is unconfirmed. Verify that the buyer carries on business, the seller is resident and not person-level exempt, the transaction is purchase of goods, and applicable statutory/notification exclusions.");
        if (!request.BuyerCarriesOnBusiness)
            return NotApplicable("The buyer is not carrying on business; the buyer condition for section 194Q is not met.");
        if (request.BuyerBusinessTurnoverInPrecedingYear <= BuyerTurnoverThreshold)
            return NotApplicable("The buyer's business turnover in the immediately preceding financial year did not exceed ₹10 crore.");
        if (!request.SellerIsResident)
            return NotApplicable("Section 194Q applies to purchases from a resident seller. Review the applicable non-resident withholding provisions separately.");
        if (request.SellerIsPersonLevelExempt)
            return NotApplicable("CBDT guidance excludes purchases from a seller who is exempt as a person under the Income-tax Act or another Parliamentary enactment; partial-income exemption is not enough.");
        if (!request.IsPurchaseOfGoods)
            return Review("The transaction has not been confirmed as a purchase of goods. Review the correct withholding provision.");
        if (request.OtherTdsProvisionApplies)
            return NotApplicable("Another TDS provision applies to this transaction; section 194Q should not be applied again.");
        if (request.EcommerceOperatorHasDeductedTds)
            return NotApplicable("Where the e-commerce operator has deducted TDS under section 194-O or the corresponding provision, the same transaction is not subjected to section 194Q.");
        if (request.OtherTcsProvisionApplies)
            return Review("A TCS provision other than section 206C(1H) may apply. The statutory interaction must be reviewed before calculating section 194Q.");
        if (request.Section206C1HTcsAlreadyCollected)
            return NotApplicable("TCS under section 206C(1H) was already collected before the buyer's section 194Q deduction; do not deduct again on the same transaction under the CBDT clarification.");
        if (!request.PanProvided)
            return Review("Seller PAN is unavailable. Verify the applicable higher-rate provision and exceptions before deduction; this calculator will not guess a rate.");

        var paymentWasEarlier = request.PaymentDate is not null &&
            (request.CreditDate is null || request.PaymentDate.Value < request.CreditDate.Value);
        var currentPurchaseForThresholdAndTds = request.GrossPurchaseAmount;
        if (!paymentWasEarlier && request.SeparatelyStatedGstAmount > 0m)
            currentPurchaseForThresholdAndTds -= request.SeparatelyStatedGstAmount;

        if (currentPurchaseForThresholdAndTds == 0m)
            return NotApplicable("The amount subject to section 194Q is zero after the separately stated GST adjustment.");

        var prior = request.PriorAggregatePurchasesFromSellerInFinancialYear;
        var cumulative = prior + currentPurchaseForThresholdAndTds;
        var excessBefore = Math.Max(0m, prior - SellerThreshold);
        var excessAfter = Math.Max(0m, cumulative - SellerThreshold);
        var taxableAmount = excessAfter - excessBefore;
        if (taxableAmount <= 0m)
            return NotApplicable("The seller-specific financial-year purchase aggregate has not exceeded ₹50 lakh.");

        var tax = decimal.Round(taxableAmount * RatePercent / 100m, 2, MidpointRounding.AwayFromZero);
        var gstExplanation = paymentWasEarlier
            ? "Payment occurred before credit; the full payment amount is used because the future GST component cannot then be identified."
            : request.SeparatelyStatedGstAmount > 0m
                ? "Credit occurred before payment and GST was separately stated; separately stated GST is excluded from the amount used for threshold and deduction."
                : "No separately stated GST adjustment was supplied.";
        return new(TdsCalculationStatus.Calculated, act, section, RatePercent, taxableAmount, tax,
            $"0.1% applied only to the incremental amount exceeding the ₹50 lakh seller-specific financial-year threshold. {gstExplanation} Advances are covered because the earlier-of-credit-or-payment test is used.",
            source);
    }
}

public sealed record Tds194QDeductionRequest(
    DateOnly? CreditDate,
    DateOnly? PaymentDate,
    decimal GrossPurchaseAmount,
    decimal SeparatelyStatedGstAmount,
    decimal PriorAggregatePurchasesFromSellerInFinancialYear,
    decimal BuyerBusinessTurnoverInPrecedingYear,
    bool BuyerCarriesOnBusiness,
    bool SellerIsResident,
    bool SellerIsPersonLevelExempt,
    bool IsPurchaseOfGoods,
    bool PanProvided,
    bool ApplicabilityConfirmed,
    bool OtherTdsProvisionApplies = false,
    bool EcommerceOperatorHasDeductedTds = false,
    bool OtherTcsProvisionApplies = false,
    bool Section206C1HTcsAlreadyCollected = false);
