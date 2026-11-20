namespace TallyAuditAssistant.Engine.Tds.Calculation;

/// <summary>
/// Calculates TDS on cash withdrawals by a bank, banking co-operative or post office
/// under section 194N (1961 Act) or section 393(3), Table Sl. No. 5 (2025 Act).
/// The caller must aggregate withdrawals for the same recipient across all accounts
/// maintained with the payer during the relevant year.
/// </summary>
public sealed class Tds194NDeductionCalculator
{
    private static readonly DateOnly EffectiveDate = new(2019, 9, 1);
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private const decimal OrdinaryThreshold = 10000000m;
    private const decimal CooperativeRecipientThreshold = 30000000m;
    private const decimal NonFilerThreshold = 2000000m;
    private const decimal RatePercent = 2m;
    private const decimal NonFilerHigherRatePercent = 5m;

    private const string OldActSource =
        "Income-tax Act, 1961 section 194N, as amended by Finance Act 2020 and Finance Act 2023; read with sections 197 and 206AA, as applicable.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 393(3), Table Sl. No. 5, read with applicable provisions of section 397 and the Income-tax Rules, 2026.";

    public TdsDeductionResult Calculate(Tds194NDeductionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WithdrawalDate is null)
            throw new ArgumentException("The cash withdrawal date is required.", nameof(request));
        if (request.CurrentCashWithdrawal < 0m || request.PriorAggregateCashWithdrawalsInYear < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Cash withdrawal values cannot be negative.");

        var date = request.WithdrawalDate.Value;
        var newAct = date >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = newAct
            ? "Section 393(3), Table Sl. No. 5"
            : "Section 194N";
        var source = newAct ? NewActSource : OldActSource;

        TdsDeductionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);
        TdsDeductionResult NotApplicable(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, source);

        if (date < EffectiveDate)
            return NotApplicable("Section 194N was not effective for cash withdrawals before 1 September 2019. Review the provisions applicable on the withdrawal date separately.");
        if (!request.PayerIsBankingCompanyCooperativeBankOrPostOffice)
            return NotApplicable("The payer is not classified as a banking company, a co-operative society carrying on banking business, or a post office. Section 194N/section 393(3), Table Sl. No. 5 does not apply to this payer.");
        if (request.RecipientIsStatutorilyExempt)
            return NotApplicable("The recipient has been identified as belonging to a statutory exclusion/notification category. Retain the exact exemption provision or notification and supporting evidence.");
        if (!request.RecipientAndAccountAggregationConfirmed)
            return Review("Confirm recipient identity and aggregate cash withdrawals across all accounts maintained by that recipient with this payer for the relevant financial/tax year. Do not calculate account-by-account.");
        if (!request.ApplicabilityConfirmed)
            return Review("Confirm that the transaction is a cash payment/withdrawal covered by the provision and that no statutory exclusion or notified reduced-rate treatment applies.");
        if (request.CurrentCashWithdrawal == 0m)
            return NotApplicable("No cash withdrawal amount was supplied.");

        var aggregate = request.PriorAggregateCashWithdrawalsInYear + request.CurrentCashWithdrawal;
        var recipientIsCooperative = request.RecipientIsCooperativeSociety;
        var ordinaryThreshold = recipientIsCooperative ? CooperativeRecipientThreshold : OrdinaryThreshold;

        if (!newAct && !request.ReturnFilingStatusForThreePrecedingYearsConfirmed)
            return Review("For a pre-1 April 2026 withdrawal, confirm whether the recipient filed returns for all three immediately preceding assessment years for which the section 139(1) due dates had expired. This status changes the old section 194N threshold and rate.");
        if (!newAct && !request.ReturnsFiledForAllThreePrecedingYears)
        {
            // The cooperative-society substitution of ₹3 crore applies to the
            // ordinary ₹1 crore limit in the non-filer proviso as well.
            var nonFilerThreshold = NonFilerThreshold;
            if (aggregate <= nonFilerThreshold)
                return NotApplicable($"The recipient is treated as a return non-filer under the old section 194N test. Aggregate withdrawals of ₹{aggregate:N2} do not exceed the applicable ₹{nonFilerThreshold:N0} threshold.");
            if (!request.PanProvided)
                return Review("PAN is unavailable. Verify section 206AA and any statutory exception/notification before deduction; this calculator deliberately does not guess the higher rate.");
            var nonFilerRate = aggregate > ordinaryThreshold ? NonFilerHigherRatePercent : RatePercent;
            var nonFilerTax = decimal.Round(request.CurrentCashWithdrawal * nonFilerRate / 100m, 2, MidpointRounding.AwayFromZero);
            return new(TdsCalculationStatus.Calculated, act, section, nonFilerRate,
                request.CurrentCashWithdrawal, nonFilerTax,
                $"{nonFilerRate}% applies to the current cash withdrawal of ₹{request.CurrentCashWithdrawal:N2}. The old section 194N non-filer threshold is ₹{nonFilerThreshold:N0}; the 5% rate applies once aggregate withdrawals exceed ₹{ordinaryThreshold:N0}. The annual aggregate of ₹{aggregate:N2} tests the threshold; prior withdrawals are not taxed again. Deduction is at the time of cash payment. Aggregate all cash withdrawals across the recipient's accounts with this payer and retain return-filing-status evidence.",
                source);
        }

        if (aggregate <= ordinaryThreshold)
            return NotApplicable($"The aggregate cash withdrawals of ₹{aggregate:N2} do not exceed the applicable ₹{ordinaryThreshold:N0} threshold.");

        if (!request.PanProvided)
            return Review("PAN is unavailable. Verify the applicable higher-rate provision under section 206AA or section 397 of the 2025 Act and any statutory exception before deduction; this calculator deliberately does not guess the rate.");

        var tax = decimal.Round(request.CurrentCashWithdrawal * RatePercent / 100m, 2, MidpointRounding.AwayFromZero);
        var transitionNote = newAct
            ? "From 1 April 2026, the new Act's section 393(3), Table Sl. No. 5 uses a 2% rate and ₹1 crore/₹3 crore threshold; it does not carry forward the old section 194N non-filer ₹20 lakh/5% tier."
            : "The old section 194N return-filer route uses the ₹1 crore threshold (₹3 crore for a recipient co-operative society) at 2%.";
        return new(TdsCalculationStatus.Calculated, act, section, RatePercent,
            request.CurrentCashWithdrawal, tax,
            $"{RatePercent}% applies to the current cash withdrawal of ₹{request.CurrentCashWithdrawal:N2} because aggregate withdrawals of ₹{aggregate:N2} exceed ₹{ordinaryThreshold:N0}. The annual aggregate tests the threshold; prior withdrawals are not taxed again. Deduction is at the time of cash payment. {transitionNote} Aggregate all cash withdrawals across the recipient's accounts with this payer and retain PAN, account, recipient-category and threshold evidence. This calculator does not prepare or file a TDS statement.",
            source);
    }
}

public sealed record Tds194NDeductionRequest(
    DateOnly? WithdrawalDate,
    decimal CurrentCashWithdrawal,
    decimal PriorAggregateCashWithdrawalsInYear,
    bool PayerIsBankingCompanyCooperativeBankOrPostOffice,
    bool RecipientIsCooperativeSociety,
    bool RecipientIsStatutorilyExempt,
    bool RecipientAndAccountAggregationConfirmed,
    bool ApplicabilityConfirmed,
    bool PanProvided,
    bool ReturnFilingStatusForThreePrecedingYearsConfirmed = true,
    bool ReturnsFiledForAllThreePrecedingYears = true);
