using System;
using System.Collections.Generic;

namespace TallyAuditAssistant.Engine.Tds.Returns;

public enum TcsBuyerReturnReliefRegime
{
    IncomeTaxAct1961BeforeApril2025,
    IncomeTaxAct1961FromApril2025,
    IncomeTaxAct2025
}

public enum TcsBuyerReturnReliefCollectionCategory
{
    Section206C1SpecifiedGoodsOrNewActTable1To5,
    Section206C1CLeaseOrNewActTable9,
    Other
}

public enum TcsBuyerReturnReliefStatus
{
    EligibleForRelief,
    NotEligible,
    ReviewRequired
}

public sealed record TcsBuyerReturnReliefRequest(
    TcsBuyerReturnReliefRegime Regime,
    TcsBuyerReturnReliefCollectionCategory CollectionCategory,
    DateOnly CollectibleDate,
    DateOnly? BuyerReturnFiledDate,
    bool? BuyerIncludedAmountInReturn,
    bool? BuyerPaidTaxDueOnDeclaredIncome,
    bool? AccountantCertificateAvailable,
    bool AssessingOfficerOrderExists);

public sealed record TcsBuyerReturnReliefAssessment(
    TcsBuyerReturnReliefStatus Status,
    bool CollectorNotDeemedInDefault,
    DateOnly? InterestEndDate,
    decimal InterestRatePercent,
    string AccountantCertificateForm,
    string SectionReference,
    IReadOnlyList<string> Findings,
    string SourceReference);

/// <summary>
/// Assesses the buyer-return proviso for failure-to-collect TCS.
/// This is a documentary review workpaper, not a legal determination or filing.
/// </summary>
public sealed class TcsBuyerReturnReliefAssessmentService
{
    private static readonly DateOnly AmendmentStart = new(2025, 4, 1);
    private static readonly DateOnly NewActStart = new(2026, 4, 1);

    public TcsBuyerReturnReliefAssessment Assess(TcsBuyerReturnReliefRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRegimeDate(request);

        var isNewAct = request.Regime == TcsBuyerReturnReliefRegime.IncomeTaxAct2025;
        var form = isNewAct ? "Form 150" : "Form 27BA";
        var section = isNewAct
            ? "Section 398(2) and 398(3)(c), Income-tax Act, 2025"
            : "Section 206C(6A) and 206C(7), Income-tax Act, 1961";
        var source = isNewAct
            ? "Income-tax Act, 2025, Section 398(2) and Section 398(3)(c); Income-tax Rules, 2026, Rule 221 and Form 150"
            : "Income-tax Act, 1961, Section 206C(6A) first proviso and Section 206C(7) first proviso; Income-tax Rules, 1962, Rule 37J and Form 27BA";

        var findings = new List<string>();
        if (request.AssessingOfficerOrderExists)
        {
            findings.Add("An Assessing Officer order exists; the demand and interest must be reviewed and applied in accordance with that order.");
            return Result(TcsBuyerReturnReliefStatus.ReviewRequired, false, null, form, section, findings, source);
        }

        if (!IsCoveredCategory(request.CollectionCategory))
        {
            findings.Add("The collection provision is outside the buyer-return relief categories expressly covered by the selected Act.");
            return Result(TcsBuyerReturnReliefStatus.NotEligible, false, null, form, section, findings, source);
        }

        var unresolvedEvidence = false;
        var conditionNotMet = false;

        if (request.BuyerReturnFiledDate is null)
        {
            findings.Add("Buyer return filing date has not been provided; verify the return acknowledgement.");
            unresolvedEvidence = true;
        }
        else if (request.BuyerReturnFiledDate.Value < request.CollectibleDate)
        {
            throw new ArgumentException("Buyer return filing date cannot be earlier than the TCS collectible date.", nameof(request));
        }

        AssessCondition(request.BuyerIncludedAmountInReturn,
            "Buyer has not established that the transaction amount was included in the income computation in the return.",
            "Buyer return evidence confirms that the transaction amount was not included in the income computation.",
            findings, ref unresolvedEvidence, ref conditionNotMet);
        AssessCondition(request.BuyerPaidTaxDueOnDeclaredIncome,
            "Evidence of payment of tax due on the income declared in the buyer's return is missing.",
            "Buyer evidence confirms that tax due on the income declared in the return has not been paid.",
            findings, ref unresolvedEvidence, ref conditionNotMet);
        AssessCondition(request.AccountantCertificateAvailable,
            $"The prescribed accountant certificate ({form}) has not been evidenced.",
            $"The prescribed accountant certificate ({form}) is not available; obtain and complete the prescribed certification before claiming relief.",
            findings, ref unresolvedEvidence, ref conditionNotMet);

        if (conditionNotMet)
            return Result(TcsBuyerReturnReliefStatus.NotEligible, false, null, form, section, findings, source);
        if (unresolvedEvidence)
            return Result(TcsBuyerReturnReliefStatus.ReviewRequired, false, null, form, section, findings, source);

        findings.Add($"The statutory buyer-return conditions and {form} are marked as satisfied; independently verify the return acknowledgement, computation, tax payment and accountant certification before relying on this assessment.");
        findings.Add("If the statutory relief is established, the collector is not deemed to be in default for the qualifying failure to collect; 1% simple interest per month or part remains payable up to the buyer's return-filing date.");
        return Result(TcsBuyerReturnReliefStatus.EligibleForRelief, true,
            request.BuyerReturnFiledDate, form, section, findings, source);
    }

    private static void AssessCondition(bool? condition, string missingMessage, string failedMessage,
        ICollection<string> findings, ref bool unresolvedEvidence, ref bool conditionNotMet)
    {
        if (condition is null)
        {
            findings.Add(missingMessage);
            unresolvedEvidence = true;
        }
        else if (!condition.Value)
        {
            findings.Add(failedMessage);
            conditionNotMet = true;
        }
    }

    private static bool IsCoveredCategory(TcsBuyerReturnReliefCollectionCategory category) =>
        category is TcsBuyerReturnReliefCollectionCategory.Section206C1SpecifiedGoodsOrNewActTable1To5
            or TcsBuyerReturnReliefCollectionCategory.Section206C1CLeaseOrNewActTable9;

    private static void ValidateRegimeDate(TcsBuyerReturnReliefRequest request)
    {
        var valid = request.Regime switch
        {
            TcsBuyerReturnReliefRegime.IncomeTaxAct1961BeforeApril2025 =>
                request.CollectibleDate < AmendmentStart,
            TcsBuyerReturnReliefRegime.IncomeTaxAct1961FromApril2025 =>
                request.CollectibleDate >= AmendmentStart && request.CollectibleDate < NewActStart,
            TcsBuyerReturnReliefRegime.IncomeTaxAct2025 =>
                request.CollectibleDate >= NewActStart,
            _ => false
        };
        if (!valid)
            throw new ArgumentException("Collectible date does not belong to the selected statutory regime. The buyer-return interest proviso is assessed for the 1961 Act period before 1 April 2026 and the 2025 Act period from 1 April 2026.", nameof(request));
    }

    private static TcsBuyerReturnReliefAssessment Result(TcsBuyerReturnReliefStatus status,
        bool notDeemedInDefault, DateOnly? interestEndDate, string form, string section,
        IReadOnlyList<string> findings, string source) =>
        new(status, notDeemedInDefault, interestEndDate, 1.0m, form, section, findings, source);
}
