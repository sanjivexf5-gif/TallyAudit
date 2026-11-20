namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Calculates basic resident-payee TDS on interest other than interest on securities
/// under section 194A or section 393(1), Table Sl. Nos. 5(ii) and 5(iii).
/// The caller must classify the interest and payer, aggregate all relevant interest
/// for the payee and tax/financial year, and confirm statutory exclusions.
/// </summary>
public sealed class Tds194ADeductionCalculator
{
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private static readonly DateOnly FinanceAct2025Start = new(2025, 4, 1);
    private const decimal RatePercent = 10m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 194A(1) and (3), as amended by Finance Act 2025 with effect from 1 April 2025; section 206AA and section 197, as applicable.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 393(1), Table Sl. No. 5(ii) and (iii), Note 1; section 402(6), as applicable.";

    public TdsDeductionResult Calculate(Tds194ADeductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CreditDate is null && request.PaymentDate is null)
            throw new ArgumentException("At least one of credit date or payment date is required.", nameof(request));
        if (request.InterestAmount < 0m || request.PriorAggregateInterestInYear < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Current and prior interest amounts cannot be negative.");

        var triggerDate = request.CreditDate is null ? request.PaymentDate!.Value
            : request.PaymentDate is null ? request.CreditDate.Value
            : request.CreditDate.Value <= request.PaymentDate.Value ? request.CreditDate.Value : request.PaymentDate.Value;
        var newAct = triggerDate >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = newAct
            ? request.PayerCategory == Tds194APayerCategory.EligibleBankingOrPostOfficeDeposit
                ? "Section 393(1), Table Sl. No. 5(ii)"
                : "Section 393(1), Table Sl. No. 5(iii)"
            : "Section 194A";
        var source = newAct ? NewActSource : OldActSource;

        TdsDeductionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);
        TdsDeductionResult NotApplicable(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, source);

        if (!request.ApplicabilityConfirmed)
            return Review("Confirm that the payee is resident, the receipt is interest other than interest on securities, the payer is within section 194A/section 393(1), and the payment is not covered by a specific exclusion or another withholding provision.");
        if (!request.PayeeIsResident)
            return NotApplicable("This calculator covers resident payees only. Review the applicable non-resident withholding provision, treaty and documentation separately.");
        if (request.IsInterestOnSecurities)
            return NotApplicable("Interest on securities is outside section 194A; classify it under section 193 or the corresponding applicable provision.");
        if (request.IsSpecificallyExemptPayment)
            return NotApplicable("The caller has identified a specific statutory exemption or non-deduction category. Retain the supporting provision and evidence in the workpaper.");
        if (request.InterestAmount == 0m)
            return NotApplicable("No interest amount was supplied.");

        if (request.PayerCategory == Tds194APayerCategory.IndividualOrHuf)
        {
            if (!request.PayerTurnoverTestConfirmed)
                return Review("For an individual/HUF payer, confirm preceding-year business turnover above ₹1 crore or professional gross receipts above ₹50 lakh. The payer is not liable under this provision if neither threshold is crossed.");
            if (!request.IndividualHufPayerIsTaxAuditLiable)
                return NotApplicable("The individual/HUF payer has not crossed the preceding-year business/profession threshold that makes the payer liable to deduct under section 194A.");
        }

        var bankOrPostOffice = request.PayerCategory == Tds194APayerCategory.EligibleBankingOrPostOfficeDeposit;
        var threshold = bankOrPostOffice
            ? request.PayeeIsSeniorCitizen ? 100000m : 50000m
            : triggerDate >= FinanceAct2025Start ? 10000m : 5000m;

        if (bankOrPostOffice && !request.DepositQualifiesForBankingThreshold)
            return Review("The payer is classified as a bank/co-operative banking society/post office, but the deposit type has not been confirmed as eligible for the special banking/post-office threshold. Classify the deposit before calculating.");

        var cumulative = request.PriorAggregateInterestInYear + request.InterestAmount;
        if (cumulative <= threshold)
            return NotApplicable($"The payee-year aggregate interest of ₹{cumulative:N2} does not exceed the applicable ₹{threshold:N0} threshold.");

        if (!request.PanProvided)
            return Review("Payee PAN is unavailable. Verify the applicable higher-rate rule under section 206AA or section 397 of the 2025 Act and any exception before deduction; this calculator will not guess the rate.");

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

        var tax = decimal.Round(request.InterestAmount * rate / 100m, 2, MidpointRounding.AwayFromZero);
        var ageNote = bankOrPostOffice
            ? request.PayeeIsSeniorCitizen
                ? "The senior-citizen threshold of ₹1,00,000 applies."
                : "The non-senior threshold of ₹50,000 applies."
            : $"The applicable non-bank threshold is ₹{threshold:N0}.";
        var creditNote = request.InterestCreditedToSuspenseOrInterestPayableAccount
            ? "Credit to the interest payable/suspense account is treated as credit to the payee; use that credit date."
            : "Deduction is due at the earlier of credit (including a deemed credit) or payment.";
        return new(TdsCalculationStatus.Calculated, act, section, rate, request.InterestAmount, tax,
            $"{rate}% applied to the full current interest payment/credit once the payee-year aggregate exceeds ₹{threshold:N0}; it is not restricted to the excess over the threshold. {ageNote} {creditNote} Verify whether any interest component belongs to a separately specified category before posting.",
            source);
    }
}

public enum Tds194APayerCategory
{
    OtherSpecifiedPayer,
    EligibleBankingOrPostOfficeDeposit,
    IndividualOrHuf
}

public sealed record Tds194ADeductionRequest(
    DateOnly? CreditDate,
    DateOnly? PaymentDate,
    decimal InterestAmount,
    decimal PriorAggregateInterestInYear,
    Tds194APayerCategory PayerCategory,
    bool PayeeIsResident,
    bool PayeeIsSeniorCitizen,
    bool PanProvided,
    bool ApplicabilityConfirmed,
    bool IsInterestOnSecurities = false,
    bool IsSpecificallyExemptPayment = false,
    bool DepositQualifiesForBankingThreshold = true,
    bool PayerTurnoverTestConfirmed = true,
    bool IndividualHufPayerIsTaxAuditLiable = true,
    bool InterestCreditedToSuspenseOrInterestPayableAccount = false,
    decimal? LowerDeductionCertificateRatePercent = null,
    string? LowerDeductionCertificateReference = null,
    DateOnly? CertificateValidFrom = null,
    DateOnly? CertificateValidTo = null);
