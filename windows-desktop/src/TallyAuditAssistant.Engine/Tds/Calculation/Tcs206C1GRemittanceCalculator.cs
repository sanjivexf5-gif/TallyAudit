namespace TallyAuditAssistant.Engine.Tds.Calculation;

public enum Tcs206C1GTransactionType
{
    LrsRemittance,
    OverseasTourPackage
}

public enum Tcs206C1GLrsPurpose
{
    Education,
    MedicalTreatment,
    Other
}

/// <summary>
/// Calculates TCS for LRS remittances and overseas tour packages under
/// section 206C(1G) or section 394(1), Table Sl. Nos. 7 and 8.
/// The caller must supply the individual's prior aggregate for the relevant
/// financial/tax year and confirm statutory applicability and exclusions.
/// </summary>
public sealed class Tcs206C1GRemittanceCalculator
{
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private const decimal AggregateThreshold = 1000000m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 206C(1G), read with section 206CC and applicable RBI Liberalised Remittance Scheme directions; CBDT Circular No. 10/2023 dated 30 June 2023 for overseas tour package scope.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 394(1), Table Sl. Nos. 7 and 8, sub-sections (4) and (5), read with the buyer exclusions table and section 398; Finance Act, 2026 amendments effective 1 April 2026.";

    public TcsCollectionResult Calculate(Tcs206C1GCollectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ReceiptDate is null && request.AmountDebitDate is null)
            throw new ArgumentException("At least one receipt date or amount-debit date is required.", nameof(request));
        if (request.CurrentAmountReceivedOrDebited < 0m || request.PriorAggregateInTaxYear < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Current and prior aggregate amounts cannot be negative.");

        var triggerDate = request.ReceiptDate is null ? request.AmountDebitDate!.Value
            : request.AmountDebitDate is null ? request.ReceiptDate.Value
            : request.ReceiptDate.Value <= request.AmountDebitDate.Value ? request.ReceiptDate.Value : request.AmountDebitDate.Value;
        var newAct = triggerDate >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = request.TransactionType switch
        {
            Tcs206C1GTransactionType.LrsRemittance => newAct
                ? "Section 394(1), Table Sl. No. 7"
                : "Section 206C(1G)(a)",
            Tcs206C1GTransactionType.OverseasTourPackage => newAct
                ? "Section 394(1), Table Sl. No. 8"
                : "Section 206C(1G)(b)",
            _ => throw new ArgumentOutOfRangeException(nameof(request.TransactionType))
        };
        var source = newAct ? NewActSource : OldActSource;

        TcsCollectionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);
        TcsCollectionResult NotApplicable(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, source);

        if (!request.CollectorEligibilityConfirmed)
            return Review(request.TransactionType == Tcs206C1GTransactionType.LrsRemittance
                ? "Confirm that the collecting entity is an authorised dealer under the RBI LRS framework."
                : "Confirm that the collecting entity is the seller of the overseas tour programme package.");
        if (!request.TransactionScopeConfirmed)
            return Review(request.TransactionType == Tcs206C1GTransactionType.LrsRemittance
                ? "Confirm that the outward remittance is covered by the RBI Liberalised Remittance Scheme and identify its actual purpose."
                : "Confirm that the sale is an overseas tour programme package. It must offer a visit outside India and include travel, hotel stay, boarding, lodging or similar/related expenditure.");
        if (!request.BuyerExemptionChecked)
            return Review("Check and document statutory buyer exclusions and any applicable Central Government notification before collection.");
        if (request.BuyerIsStatutorilyExcluded)
            return NotApplicable("The buyer/remitter has been confirmed as falling within a statutory or notified exclusion. Retain the exact category and supporting evidence.");
        if (request.BuyerHasDeductedTaxUnderAnotherProvision)
            return NotApplicable("Section 394(5) / the corresponding section 206C(1G) exception applies where the buyer is liable to deduct tax under another provision and has actually deducted it. Retain the deduction evidence.");
        if (request.CurrentAmountReceivedOrDebited == 0m)
            return NotApplicable("No current receipt/debit event amount was supplied.");
        if (!request.PanProvided)
            return Review("PAN/Aadhaar status is missing or unverified. Apply the applicable higher-rate provision only after validating the governing Act, PAN/Aadhaar status and current rules; this calculator deliberately does not assume the default rate.");
        if (request.TransactionType == Tcs206C1GTransactionType.LrsRemittance)
        {
            if (request.LrsPurpose == Tcs206C1GLrsPurpose.Education &&
                request.IsEducationLoanFromQualifyingFinancialInstitution)
                return NotApplicable("No TCS is collectible on the qualifying education-loan amount under section 206C(1G)(a) / section 394(4)(b). Retain lender and loan-purpose evidence.");
            if (request.LrsPurpose == Tcs206C1GLrsPurpose.Education &&
                request.IsEducationLoanFromQualifyingFinancialInstitution is false &&
                !request.LrsPurposeConfirmed)
                return Review("Confirm the remittance purpose and whether the education-loan exemption applies.");
            if (!request.LrsPurposeConfirmed)
                return Review("Confirm whether the LRS remittance is for education/medical treatment or another purpose. The applicable rate differs.");
            if (request.OverseasTourPackageTcsAlreadyCollected)
                return NotApplicable("The amount has already borne TCS collected by the overseas tour-package seller. Do not collect duplicate LRS TCS on the same amount.");
        }

        decimal taxableAmount;
        decimal rate;
        decimal tax;
        if (request.TransactionType == Tcs206C1GTransactionType.LrsRemittance)
        {
            taxableAmount = IncrementalAmountAboveThreshold(request.PriorAggregateInTaxYear, request.CurrentAmountReceivedOrDebited);
            if (taxableAmount == 0m)
                return NotApplicable($"The aggregate LRS remittances before this event were ₹{request.PriorAggregateInTaxYear:N2}; after this event they do not exceed ₹{AggregateThreshold:N0}. The ₹10 lakh threshold is shared across LRS remittances in the tax year, not separately reset for each remittance or purpose.");
            rate = request.LrsPurpose is Tcs206C1GLrsPurpose.Education or Tcs206C1GLrsPurpose.MedicalTreatment
                ? (newAct ? 2m : 5m)
                : 20m;
            tax = decimal.Round(taxableAmount * rate / 100m, 2, MidpointRounding.AwayFromZero);
            return new(TdsCalculationStatus.Calculated, act, section, rate, taxableAmount, tax,
                $"The annual aggregate LRS amount crosses ₹{AggregateThreshold:N0}. TCS applies only to the incremental portion of this event above the shared threshold: ₹{taxableAmount:N2} at {rate:N2}%. Prior aggregate supplied: ₹{request.PriorAggregateInTaxYear:N2}; current event: ₹{request.CurrentAmountReceivedOrDebited:N2}. The threshold is common across LRS purposes. Qualifying education-loan remittances are excluded, and no duplicate TCS is collected on amounts already covered by an overseas tour-package seller. Collection is at the earlier of debit or receipt. Retain purpose, AD, remittance, annual aggregate, PAN/Aadhaar and exemption evidence.", source);
        }

        if (request.OverseasTourPackageTcsAlreadyCollected)
            return NotApplicable("TCS has already been collected by the overseas tour-package seller. Do not collect it a second time on the same amount.");
        if (newAct)
        {
            taxableAmount = request.CurrentAmountReceivedOrDebited;
            rate = 2m;
        }
        else
        {
            taxableAmount = request.CurrentAmountReceivedOrDebited;
            rate = 5m;
            var priorBelowThreshold = Math.Max(0m, AggregateThreshold - request.PriorAggregateInTaxYear);
            var currentAtLowerRate = Math.Min(taxableAmount, priorBelowThreshold);
            var currentAtHigherRate = taxableAmount - currentAtLowerRate;
            tax = decimal.Round(currentAtLowerRate * 5m / 100m + currentAtHigherRate * 20m / 100m, 2, MidpointRounding.AwayFromZero);
            if (taxableAmount > 0m)
                return new(TdsCalculationStatus.Calculated, act, section, null, taxableAmount, tax,
                    $"Under the old Act, overseas tour-package TCS is 5% on the portion of the tax-year aggregate up to ₹{AggregateThreshold:N0} and 20% on the portion above it. Prior aggregate supplied: ₹{request.PriorAggregateInTaxYear:N2}; current event: ₹{request.CurrentAmountReceivedOrDebited:N2}. TCS for this event is ₹{tax:N2}. A single international ticket or hotel booking alone is not necessarily a tour package; confirm package scope and retain component details. Collection is at the earlier of debit or receipt.", source);
        }
        tax = decimal.Round(taxableAmount * rate / 100m, 2, MidpointRounding.AwayFromZero);
        return new(TdsCalculationStatus.Calculated, act, section, rate, taxableAmount, tax,
            $"From 1 April 2026, TCS on an overseas tour programme package is 2% on the full current event amount, with no ₹10 lakh threshold. Confirm that the package includes a visit outside India and the prescribed travel/accommodation or related components. Do not collect duplicate TCS where already collected on this same amount. Collection is at the earlier of debit or receipt. Retain package invoice, component details, seller/buyer eligibility, PAN/Aadhaar and collection evidence.", source);
    }

    private static decimal IncrementalAmountAboveThreshold(decimal prior, decimal current)
    {
        var priorExcess = Math.Max(0m, prior - AggregateThreshold);
        var aggregateExcess = Math.Max(0m, prior + current - AggregateThreshold);
        return aggregateExcess - priorExcess;
    }
}

public sealed record Tcs206C1GCollectionRequest(
    Tcs206C1GTransactionType TransactionType,
    DateOnly? ReceiptDate,
    DateOnly? AmountDebitDate,
    decimal PriorAggregateInTaxYear,
    decimal CurrentAmountReceivedOrDebited,
    bool CollectorEligibilityConfirmed,
    bool TransactionScopeConfirmed,
    bool BuyerExemptionChecked,
    bool BuyerIsStatutorilyExcluded,
    bool PanProvided,
    Tcs206C1GLrsPurpose LrsPurpose = Tcs206C1GLrsPurpose.Other,
    bool LrsPurposeConfirmed = true,
    bool IsEducationLoanFromQualifyingFinancialInstitution = false,
    bool OverseasTourPackageTcsAlreadyCollected = false,
    bool BuyerHasDeductedTaxUnderAnotherProvision = false);
