namespace TallyAuditAssistant.Engine.Tds.Calculation;

public enum Tcs206C1CContractType
{
    ParkingLot,
    TollPlaza,
    Mine,
    Quarry
}

/// <summary>
/// Calculates TCS on business-use parking lot, toll plaza, mine or quarry
/// lease/licence/contract receipts under section 206C(1C) or section 394(1),
/// Table Sl. No. 9. This is a workpaper calculation only.
/// </summary>
public sealed class Tcs206C1CLeaseCalculator
{
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private const decimal StandardRatePercent = 2m;
    private const string OldActSource =
        "Income-tax Act, 1961 section 206C(1C), section 206CC and section 206C(9); excludes public sector company licensee/lessee and mineral-oil mining/quarrying.";
    private const string NewActSource =
        "Income-tax Act, 2025 section 394(1), Table Sl. No. 9, section 397 and section 395; applies from 1 April 2026.";

    public TcsCollectionResult Calculate(Tcs206C1CLeaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ReceiptDate is null && request.AmountDebitDate is null)
            throw new ArgumentException("At least one receipt date or amount-debit date is required.", nameof(request));
        if (request.AmountPayableOrReceived < 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "The amount payable/received cannot be negative.");
        if (request.LowerRateCertificateRatePercent is < 0m or > 100m)
            throw new ArgumentOutOfRangeException(nameof(request), "The lower-rate certificate percentage must be between 0 and 100.");

        var triggerDate = request.ReceiptDate is null ? request.AmountDebitDate!.Value
            : request.AmountDebitDate is null ? request.ReceiptDate.Value
            : request.ReceiptDate.Value <= request.AmountDebitDate.Value ? request.ReceiptDate.Value : request.AmountDebitDate.Value;
        var newAct = triggerDate >= NewActStart;
        var act = newAct ? "Income-tax Act, 2025" : "Income-tax Act, 1961";
        var section = newAct ? "Section 394(1), Table Sl. No. 9" : "Section 206C(1C)";
        var source = newAct ? NewActSource : OldActSource;

        TcsCollectionResult Review(string explanation) =>
            new(TdsCalculationStatus.ReviewRequired, act, section, null, 0m, 0m, explanation, source);
        TcsCollectionResult NotApplicable(string explanation) =>
            new(TdsCalculationStatus.BelowThreshold, act, section, 0m, 0m, 0m, explanation, source);

        if (!request.GrantorEligibilityConfirmed)
            return Review("Confirm that the person granting the lease, licence, contract or other right/interest is responsible for collection under section 206C(1C). Retain the executed agreement and evidence of the rights transferred.");
        if (!request.LicenseeEligibilityChecked)
            return Review("Confirm the identity and legal status of the licensee/lessee and that the right is used for business purposes. Do not infer these conditions from the ledger name alone.");
        if (request.LicenseeIsPublicSectorCompany)
            return NotApplicable("The licensee/lessee is confirmed as a public sector company, which is excluded from section 206C(1C). Retain evidence of its PSU status.");
        if (!request.UsedForBusiness)
            return NotApplicable("The parking lot, toll plaza, mine or quarry right is not confirmed as being used for the purpose of business. Section 206C(1C) does not apply on the facts supplied.");
        if (request.ContractType is Tcs206C1CContractType.Mine or Tcs206C1CContractType.Quarry &&
            request.InvolvesMineralOil)
            return NotApplicable("Mining or quarrying of mineral oil, including petroleum and natural gas, is excluded from this provision. Review the substance of the activity and retain technical/agreement evidence.");
        if (request.AmountPayableOrReceived == 0m)
            return NotApplicable("No amount payable/received was supplied for this debit/receipt event.");
        if (!request.PanProvided)
            return Review("Licensee/lessee PAN is missing or unverified. The higher-rate rule under section 206CC / section 397 may apply (higher of twice the normal rate or 5%, subject to the statutory cap). Verify PAN/Aadhaar status and applicable exceptions before posting; no higher rate is assumed automatically.");

        var rate = StandardRatePercent;
        if (request.LowerRateCertificatePresented)
        {
            if (!request.LowerRateCertificateValid || request.LowerRateCertificateRatePercent is null)
                return Review("A lower/nil collection certificate has been presented but its validity, scope, rate and effective dates are not confirmed. Verify the certificate under section 206C(9) / section 395 before applying it.");
            rate = request.LowerRateCertificateRatePercent.Value;
        }

        var tax = decimal.Round(request.AmountPayableOrReceived * rate / 100m, 2, MidpointRounding.AwayFromZero);
        var contractName = request.ContractType switch
        {
            Tcs206C1CContractType.ParkingLot => "parking lot",
            Tcs206C1CContractType.TollPlaza => "toll plaza",
            Tcs206C1CContractType.Mine => "mine",
            Tcs206C1CContractType.Quarry => "quarry",
            _ => throw new ArgumentOutOfRangeException(nameof(request.ContractType))
        };
        var certificateNote = request.LowerRateCertificatePresented
            ? $" A verified lower-rate certificate has been applied at {rate:N2}%."
            : string.Empty;
        var transition = newAct
            ? "Section 394(1), Table Sl. No. 9 applies from 1 April 2026."
            : "Section 206C(1C) applies before 1 April 2026.";

        return new(TdsCalculationStatus.Calculated, act, section, rate,
            request.AmountPayableOrReceived, tax,
            $"TCS applies at {rate:N2}% on the full amount payable/received for the business-use {contractName} right for this debit/receipt event. No monetary threshold applies. Collect at the earlier of amount debit or receipt. {transition} Retain the agreement, asset/right classification, business-use confirmation, licensee/lessee legal status, amount schedule, event dates and PAN evidence.{certificateNote}",
            source);
    }
}

public sealed record Tcs206C1CLeaseRequest(
    Tcs206C1CContractType ContractType,
    DateOnly? ReceiptDate,
    DateOnly? AmountDebitDate,
    decimal AmountPayableOrReceived,
    bool GrantorEligibilityConfirmed,
    bool LicenseeEligibilityChecked,
    bool LicenseeIsPublicSectorCompany,
    bool UsedForBusiness,
    bool InvolvesMineralOil,
    bool PanProvided,
    bool LowerRateCertificatePresented = false,
    bool LowerRateCertificateValid = false,
    decimal? LowerRateCertificateRatePercent = null);
