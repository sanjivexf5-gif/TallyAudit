namespace TallyAuditAssistant.Engine.Tds.Calculation;

public enum Tcs206C1GoodsCategory
{
    AlcoholicLiquorForHumanConsumption,
    TenduLeaves,
    TimberUnderForestLease,
    TimberOtherThanForestLease,
    OtherForestProduceUnderForestLease,
    Scrap,
    Coal,
    Lignite,
    IronOre
}

/// <summary>
/// Calculates TCS on sale of specified goods under section 206C(1) or
/// section 394(1), Table Sl. Nos. 1 to 5. This is a workpaper calculation;
/// seller/buyer eligibility and declarations must be evidenced by the user.
/// </summary>
public sealed class Tcs206C1SpecifiedGoodsCalculator
{
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private static readonly DateOnly ForestProduceRestrictionDate = new(2025, 4, 1);

    private const string OldActSource =
        "Income-tax Act, 1961 section 206C(1), as amended by Finance Act, 2025 section 72 (effective 1 April 2025); sections 206CC and 206C(9); Form 27C.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 394(1), Table Sl. Nos. 1 to 5, section 394(2) and (3), section 397 and section 395; Income-tax Rules, 2026 Form 127.";

    public TcsCollectionResult Calculate(Tcs206C1SpecifiedGoodsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ReceiptDate is null && request.AmountDebitDate is null)
            throw new ArgumentException("At least one receipt date or amount-debit date is required.", nameof(request));
        if (request.CurrentAmountReceivedOrDebited < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "The current receipt/debit amount cannot be negative.");
        if (request.LowerRateCertificateRatePercent is < 0m or > 100m)
            throw new ArgumentOutOfRangeException(nameof(request), "The lower-rate certificate percentage must be between 0 and 100.");

        var triggerDate = request.ReceiptDate is null ? request.AmountDebitDate!.Value
            : request.AmountDebitDate is null ? request.ReceiptDate.Value
            : request.ReceiptDate.Value <= request.AmountDebitDate.Value ? request.ReceiptDate.Value : request.AmountDebitDate.Value;
        var newAct = triggerDate >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = newAct
            ? "Section 394(1), Table Sl. No. " + TableNumber(request.GoodsCategory)
            : "Section 206C(1)";
        var source = newAct ? NewActSource : OldActSource;

        TcsCollectionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);
        TcsCollectionResult NotApplicable(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, source);

        if (!request.SellerEligibilityConfirmed)
            return Review("Confirm that the seller is within the statutory definition for section 206C(1), including the prescribed entity types and the preceding-year turnover test for an individual/HUF. Retain supporting turnover and entity evidence.");
        if (!request.BuyerEligibilityChecked)
            return Review("Confirm the buyer falls within the statutory definition. Review exclusions including Government, specified diplomatic/foreign representations, public sector company, club and retail purchase for personal consumption, as applicable. A buyer's name or ledger group alone is insufficient.");
        if (request.BuyerIsStatutorilyExcluded)
            return NotApplicable("The buyer has been confirmed as falling within a statutory exclusion from section 206C(1)/section 394(1), Table Sl. Nos. 1 to 5. Retain the exact exclusion category and evidence.");
        if (request.CurrentAmountReceivedOrDebited == 0m)
            return NotApplicable("No current receipt/debit event amount was supplied.");

        if (request.GoodsCategory == Tcs206C1GoodsCategory.OtherForestProduceUnderForestLease &&
            triggerDate < ForestProduceRestrictionDate)
        {
            // Before 1 April 2025, the old table covered other forest produce whether or not
            // obtained under a forest lease. The post-amendment category remains in scope
            // only where it is obtained under a forest lease.
        }
        else if (request.GoodsCategory == Tcs206C1GoodsCategory.OtherForestProduceUnderForestLease &&
                 !request.GoodsObtainedUnderForestLease)
        {
            return NotApplicable("From 1 April 2025, other forest produce (excluding timber and tendu leaves) is covered only when obtained under a forest lease. Confirm the legal character and source of the goods.");
        }

        if (!newAct && triggerDate < ForestProduceRestrictionDate &&
            request.GoodsCategory == Tcs206C1GoodsCategory.OtherForestProduceUnderForestLease &&
            !request.GoodsObtainedUnderForestLease)
        {
            // The pre-1 April 2025 law covered this category even without a forest lease.
        }

        if (request.Form127Or27CDeclarationPresented)
        {
            if (!request.DeclarationValid || !request.PanProvided)
                return Review(newAct
                    ? "The Form 127 declaration cannot be relied on unless it is complete, valid and supported by the buyer's valid PAN. Verify resident-buyer status, permitted goods, intended manufacturing/processing/production or power-generation use (not trading), and the seller's reporting obligation."
                    : "The Form 27C declaration cannot be relied on unless it is complete, valid and supported by the buyer's PAN. Verify resident-buyer status, permitted goods, intended manufacturing/processing/production or power-generation use (not trading), and the seller's reporting obligation.");
            if (request.BuyerIsResident && request.DeclaredUseIsManufacturingProcessingProductionOrPower && !request.DeclaredUseIsTrading)
                return NotApplicable(newAct
                    ? "A valid Form 127 declaration has been confirmed for a resident buyer using the goods for manufacturing, processing, producing articles/things or generating power and not for trading. Do not collect TCS; retain the declaration and file the seller's copy by the statutory deadline."
                    : "A valid Form 27C declaration has been confirmed for a resident buyer using the goods for manufacturing, processing, producing articles/things or generating power and not for trading. Do not collect TCS; retain the declaration and file the seller's copy by the statutory deadline.");
            return Review("The declaration has been presented, but the statutory resident-buyer and qualifying-use conditions are not all confirmed. Do not treat it as an exemption until verified.");
        }

        if (!request.PanProvided)
            return Review("Buyer PAN is missing or unverified. Under section 206CC / section 397, the higher-rate rule may apply (higher of twice the normal rate or 5%, subject to the statutory 20% cap). Verify PAN/Aadhaar status and applicable exceptions before posting; this calculator does not silently assume PAN compliance.");

        var statutoryRate = GetRate(request.GoodsCategory, newAct, triggerDate);
        var rate = statutoryRate;
        if (request.LowerRateCertificatePresented)
        {
            if (!request.LowerRateCertificateValid || request.LowerRateCertificateRatePercent is null)
                return Review("A lower/nil collection certificate has been presented but its validity, scope, rate and effective dates have not been confirmed. Verify the certificate under section 206C(9) / section 395 before applying it.");
            rate = request.LowerRateCertificateRatePercent.Value;
        }

        var tax = decimal.Round(request.CurrentAmountReceivedOrDebited * rate / 100m, 2, MidpointRounding.AwayFromZero);
        var category = DisplayName(request.GoodsCategory);
        var transition = newAct
            ? "Section 394 applies from 1 April 2026 and collection is at the earlier of debit or receipt."
            : "Section 206C(1) applies before 1 April 2026 and collection is at the earlier of debit or receipt.";
        var forestNote = request.GoodsCategory == Tcs206C1GoodsCategory.OtherForestProduceUnderForestLease
            ? "Forest produce must meet the definition under the applicable State law or the Indian Forest Act, 1927."
            : string.Empty;
        var certificateNote = request.LowerRateCertificatePresented
            ? $" A verified lower-rate certificate has been applied at {rate:N2}%."
            : string.Empty;

        return new(TdsCalculationStatus.Calculated, act, section, rate,
            request.CurrentAmountReceivedOrDebited, tax,
            $"TCS applies to sale of {category} at {rate:N2}% on this receipt/debit event amount of ₹{request.CurrentAmountReceivedOrDebited:N2}. There is no monetary threshold under this provision. {transition} {forestNote} Verify the buyer definition and any applicable exclusion, and retain goods classification, source/lease evidence, invoice, receipt/debit dates, seller eligibility and PAN evidence.{certificateNote}",
            source);
    }

    private static decimal GetRate(Tcs206C1GoodsCategory category, bool newAct, DateOnly triggerDate)
    {
        if (newAct)
        {
            // Finance Act, 2026 rationalised rates from 1 April 2026.
            return category switch
            {
                Tcs206C1GoodsCategory.AlcoholicLiquorForHumanConsumption or
                Tcs206C1GoodsCategory.TenduLeaves or
                Tcs206C1GoodsCategory.Scrap or
                Tcs206C1GoodsCategory.Coal or
                Tcs206C1GoodsCategory.Lignite or
                Tcs206C1GoodsCategory.IronOre => 2m,
                Tcs206C1GoodsCategory.TimberUnderForestLease or
                Tcs206C1GoodsCategory.TimberOtherThanForestLease or
                Tcs206C1GoodsCategory.OtherForestProduceUnderForestLease => 2m,
                _ => throw new ArgumentOutOfRangeException(nameof(category))
            };
        }

        if (triggerDate < ForestProduceRestrictionDate &&
            category is Tcs206C1GoodsCategory.TimberUnderForestLease or
                Tcs206C1GoodsCategory.TimberOtherThanForestLease or
                Tcs206C1GoodsCategory.OtherForestProduceUnderForestLease)
            return 2.5m;

        return category switch
        {
            Tcs206C1GoodsCategory.AlcoholicLiquorForHumanConsumption => 1m,
            Tcs206C1GoodsCategory.TenduLeaves => 5m,
            Tcs206C1GoodsCategory.TimberUnderForestLease or
            Tcs206C1GoodsCategory.TimberOtherThanForestLease or
            Tcs206C1GoodsCategory.OtherForestProduceUnderForestLease => 2m,
            Tcs206C1GoodsCategory.Scrap or
            Tcs206C1GoodsCategory.Coal or
            Tcs206C1GoodsCategory.Lignite or
            Tcs206C1GoodsCategory.IronOre => 1m,
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
    }

    private static int TableNumber(Tcs206C1GoodsCategory category) => category switch
    {
        Tcs206C1GoodsCategory.AlcoholicLiquorForHumanConsumption => 1,
        Tcs206C1GoodsCategory.TenduLeaves => 2,
        Tcs206C1GoodsCategory.TimberUnderForestLease or
        Tcs206C1GoodsCategory.TimberOtherThanForestLease or
        Tcs206C1GoodsCategory.OtherForestProduceUnderForestLease => 3,
        Tcs206C1GoodsCategory.Scrap => 4,
        Tcs206C1GoodsCategory.Coal or
        Tcs206C1GoodsCategory.Lignite or
        Tcs206C1GoodsCategory.IronOre => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    private static string DisplayName(Tcs206C1GoodsCategory category) => category switch
    {
        Tcs206C1GoodsCategory.AlcoholicLiquorForHumanConsumption => "alcoholic liquor for human consumption",
        Tcs206C1GoodsCategory.TenduLeaves => "tendu leaves",
        Tcs206C1GoodsCategory.TimberUnderForestLease => "timber obtained under a forest lease",
        Tcs206C1GoodsCategory.TimberOtherThanForestLease => "timber obtained otherwise than under a forest lease",
        Tcs206C1GoodsCategory.OtherForestProduceUnderForestLease => "other forest produce obtained under a forest lease",
        Tcs206C1GoodsCategory.Scrap => "scrap",
        Tcs206C1GoodsCategory.Coal => "coal",
        Tcs206C1GoodsCategory.Lignite => "lignite",
        Tcs206C1GoodsCategory.IronOre => "iron ore",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
}

public sealed record Tcs206C1SpecifiedGoodsRequest(
    Tcs206C1GoodsCategory GoodsCategory,
    DateOnly? ReceiptDate,
    DateOnly? AmountDebitDate,
    decimal CurrentAmountReceivedOrDebited,
    bool SellerEligibilityConfirmed,
    bool BuyerEligibilityChecked,
    bool BuyerIsStatutorilyExcluded,
    bool PanProvided,
    bool GoodsObtainedUnderForestLease = false,
    bool Form127Or27CDeclarationPresented = false,
    bool DeclarationValid = false,
    bool BuyerIsResident = false,
    bool DeclaredUseIsManufacturingProcessingProductionOrPower = false,
    bool DeclaredUseIsTrading = true,
    bool LowerRateCertificatePresented = false,
    bool LowerRateCertificateValid = false,
    decimal? LowerRateCertificateRatePercent = null);
