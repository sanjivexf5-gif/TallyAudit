namespace TallyAuditAssistant.Engine.Tds.Calculation;

public enum TdsRentAssetCategory
{
    LandBuildingOrAppurtenant,
    MachineryPlantOrEquipment,
    FurnitureOrFittings
}

/// <summary>
/// Calculates basic resident-payee rent withholding under section 194-I / 194-IB
/// and the corresponding section 393(1), Table Sl. No. 2 entries.
/// Caller must confirm residency, payment classification, payer turnover status,
/// and any statutory exclusion (including qualifying REIT payments).
/// For a non-specified payer, RentAmountForMonth must represent the final month
/// of the tax year or tenancy because the special rule defers deduction to then.
/// </summary>
public sealed class TdsRentDeductionCalculator
{
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private const decimal MonthlyThreshold = 50000m;

    private const string OldActSource =
        "Income-tax Act, 1961 sections 194-I and 194-IB, as amended by Finance Act 2025; Income Tax Department rent-TDS guidance.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 393(1), Table Sl. No. 2(i) and 2(ii), read with the definition of specified person.";

    public TdsDeductionResult Calculate(TdsRentDeductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CreditDate is null && request.PaymentDate is null)
            throw new ArgumentException("At least one of credit date or payment date is required.", nameof(request));
        if (request.RentAmountForMonth < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Monthly rent cannot be negative.");

        var triggerDate = request.CreditDate is null ? request.PaymentDate!.Value
            : request.PaymentDate is null ? request.CreditDate.Value
            : request.CreditDate.Value <= request.PaymentDate.Value ? request.CreditDate.Value : request.PaymentDate.Value;
        var newAct = triggerDate >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var source = newAct ? NewActSource : OldActSource;

        var nonSpecifiedPayer = request.PayerIsIndividualOrHuf && !request.PayerIsSpecifiedPerson;
        var isLandOrBuilding = request.AssetCategory == TdsRentAssetCategory.LandBuildingOrAppurtenant;

        string section;
        decimal rate;
        if (newAct)
        {
            if (nonSpecifiedPayer)
            {
                section = "Section 393(1), Table Sl. No. 2(i)";
                if (!isLandOrBuilding)
                    return Review("The non-specified-person rent entry is limited to land, building and land appurtenant. Machinery, plant, equipment, furniture and fittings require a different applicability review.");
                rate = 2m;
            }
            else
            {
                section = "Section 393(1), Table Sl. No. 2(ii)";
                rate = request.AssetCategory == TdsRentAssetCategory.MachineryPlantOrEquipment ? 2m : 10m;
            }
        }
        else if (nonSpecifiedPayer)
        {
            section = "Section 194-IB";
            if (!isLandOrBuilding)
                return Review("Before 1 April 2026, section 194-IB applies to rent for land, building or both, not machinery, plant, equipment, furniture or fittings. Verify whether another provision applies.");
            rate = 2m;
        }
        else
        {
            section = "Section 194-I";
            rate = request.AssetCategory == TdsRentAssetCategory.MachineryPlantOrEquipment ? 2m : 10m;
        }

        TdsDeductionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);

        if (!request.ApplicabilityConfirmed)
            return Review("TDS applicability is unconfirmed. Verify resident-payee status, rent classification, payer entity/turnover status and applicable exclusions (including qualifying REIT payments).");
        if (!request.PanProvided)
            return Review("Deductee PAN is unavailable. Verify the applicable higher-rate provision, statutory cap and exceptions before deduction; this calculator will not guess a rate.");
        if (request.RentAmountForMonth == 0m)
            return new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, "No monthly rent amount was supplied.", source);
        if (request.RentAmountForMonth <= MonthlyThreshold)
            return new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m,
                "Monthly rent does not exceed ₹50,000; no deduction is calculated for this month.", source);

        if (nonSpecifiedPayer && !request.IsLastMonthOfTaxYearOrTenancy)
            return new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m,
                "For a non-specified payer, deduction is deferred to the last month of the tax year or the last month of tenancy. No deduction is due at this point.", source);

        if (request.LowerDeductionCertificateRatePercent is decimal certificateRate)
        {
            if (string.IsNullOrWhiteSpace(request.LowerDeductionCertificateReference) ||
                request.CertificateValidFrom is null || request.CertificateValidTo is null ||
                triggerDate < request.CertificateValidFrom || triggerDate > request.CertificateValidTo ||
                certificateRate < 0m || certificateRate > rate)
                return Review("Lower/nil deduction certificate details are incomplete, invalid, outside their validity period, or exceed the ordinary rate.");
            rate = certificateRate;
        }

        var tax = decimal.Round(request.RentAmountForMonth * rate / 100m, 2, MidpointRounding.AwayFromZero);
        return new(TdsCalculationStatus.Calculated, act, section, rate, request.RentAmountForMonth, tax,
            nonSpecifiedPayer
                ? "The 2% non-specified-payer rent rate is applied to the final month's rent after the ₹50,000 monthly threshold is exceeded."
                : "Specified-person rent rate applied after the ₹50,000 monthly threshold was exceeded. Verify the asset category and statutory exclusions.",
            source);
    }
}

public sealed record TdsRentDeductionRequest(
    DateOnly? CreditDate,
    DateOnly? PaymentDate,
    decimal RentAmountForMonth,
    TdsRentAssetCategory AssetCategory,
    bool PayerIsIndividualOrHuf,
    bool PayerIsSpecifiedPerson,
    bool PanProvided,
    bool ApplicabilityConfirmed,
    bool IsLastMonthOfTaxYearOrTenancy,
    decimal? LowerDeductionCertificateRatePercent = null,
    string? LowerDeductionCertificateReference = null,
    DateOnly? CertificateValidFrom = null,
    DateOnly? CertificateValidTo = null);
