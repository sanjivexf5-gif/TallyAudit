namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Calculates resident-payee withholding on eligible income in respect of specified
/// mutual-fund/UTI units under section 194K or section 393(1), Table Sl. No. 4(i).
/// Capital-gain income and non-resident payments are deliberately excluded.
/// </summary>
public sealed class Tds194KDeductionCalculator
{
    private static readonly DateOnly ThresholdChangeDate = new(2025, 4, 1);
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private const decimal RatePercent = 10m;
    private const decimal EarlierThreshold = 5000m;
    private const decimal CurrentThreshold = 10000m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 194K, inserted by Finance Act, 2020 and amended by Finance Act, 2025 (threshold effective 1 April 2025); sections 197 and 206AA, as applicable.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 393(1), Table Sl. No. 4(i), read with section 397 and the applicable Income-tax Rules, 2026; capital-gain exclusion under the Table for No Deduction at Source.";

    public TdsDeductionResult Calculate(Tds194KDeductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CreditDate is null && request.PaymentDate is null)
            throw new ArgumentException("At least one of credit date or payment date is required.", nameof(request));
        if (request.AmountPaidOrCredited < 0m || request.PriorAggregatePaidOrCreditedInFinancialYear < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Payment and prior aggregate amounts cannot be negative.");

        var triggerDate = request.CreditDate is null ? request.PaymentDate!.Value
            : request.PaymentDate is null ? request.CreditDate.Value
            : request.CreditDate.Value <= request.PaymentDate.Value ? request.CreditDate.Value : request.PaymentDate.Value;
        var newAct = triggerDate >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = newAct ? "Section 393(1), Table Sl. No. 4(i)" : "Section 194K";
        var source = newAct ? NewActSource : OldActSource;
        var threshold = triggerDate < ThresholdChangeDate ? EarlierThreshold : CurrentThreshold;

        TdsDeductionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);
        TdsDeductionResult NotApplicable(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, source);

        if (!request.ApplicabilityConfirmed)
            return Review("Confirm that the payment is income in respect of units of a qualifying mutual fund under Schedule VII (Table Sl. No. 20 or 21), units from the Administrator of the specified undertaking, or units from the specified company. Ordinary redemption proceeds/capital gains are not automatically covered.");
        if (!request.RecipientIsResident)
            return NotApplicable("Section 194K and section 393(1) Table Sl. No. 4(i) cover resident recipients. Review the applicable non-resident withholding provision and treaty separately.");
        if (request.IncomeIsCapitalGain)
            return NotApplicable("Income of the nature of capital gains is expressly excluded from withholding under section 194K and the corresponding new-Act no-deduction table.");
        if (request.AmountPaidOrCredited == 0m)
            return NotApplicable("No eligible income amount was supplied.");
        if (!request.PanProvided)
            return Review("PAN is unavailable. Verify the applicable higher-rate rule under section 206AA or section 397(2), and any statutory exception, before deduction; this calculator does not guess the default rate.");

        var aggregate = request.PriorAggregatePaidOrCreditedInFinancialYear + request.AmountPaidOrCredited;
        if (aggregate <= threshold)
            return NotApplicable($"The financial-year aggregate of eligible income is ₹{aggregate:N2}, which does not exceed the applicable ₹{threshold:N0} threshold.");

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

        var tax = decimal.Round(request.AmountPaidOrCredited * rate / 100m, 2, MidpointRounding.AwayFromZero);
        var thresholdNote = triggerDate < ThresholdChangeDate
            ? "The ₹5,000 annual threshold applies before 1 April 2025."
            : "The ₹10,000 annual threshold applies from 1 April 2025.";
        return new(TdsCalculationStatus.Calculated, act, section, rate, request.AmountPaidOrCredited, tax,
            $"The eligible income aggregate of ₹{aggregate:N2} exceeded the applicable annual threshold. TDS is calculated at 10% on this current credit/payment event of ₹{request.AmountPaidOrCredited:N2}; prior events are used for threshold testing and are not taxed again. {thresholdNote} Deduct at the earlier of credit (including suspense-account credit) or payment. Confirm that the amount is income and not capital gain, and retain payee/PAN/certificate evidence.",
            source);
    }
}

public sealed record Tds194KDeductionRequest(
    DateOnly? CreditDate,
    DateOnly? PaymentDate,
    decimal AmountPaidOrCredited,
    decimal PriorAggregatePaidOrCreditedInFinancialYear,
    bool RecipientIsResident,
    bool IncomeIsCapitalGain,
    bool PanProvided,
    bool ApplicabilityConfirmed,
    decimal? LowerDeductionCertificateRatePercent = null,
    string? LowerDeductionCertificateReference = null,
    DateOnly? CertificateValidFrom = null,
    DateOnly? CertificateValidTo = null);
