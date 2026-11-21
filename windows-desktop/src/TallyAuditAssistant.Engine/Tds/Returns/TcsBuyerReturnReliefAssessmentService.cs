using System;
using System.Collections.Generic;

namespace TallyAuditAssistant.Engine.Tds.Returns;

public enum TcsBuyerReturnReliefRegime
{
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
    bool BuyerIncludedAmountInReturn,
    bool BuyerPaidTaxDueOnDeclaredIncome,
    bool AccountantCertificateAvailable,
    bool AssessingOfficerOrderExists);

public sealed record TcsBuyerReturnReliefAssessment(
    TcsBuyerReturnReliefStatus Status,
    bool CollectorNotDeemedInDefault,
    DateOnly? InterestEndDate,
    decimal InterestRatePercent,
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

        var section = request.Regime == TcsBuyerReturnReliefRegime.IncomeTaxAct2025
            ? "Section 398(2) and 398(3)(c), Income-tax Act, 2025"
            : "Section 206C(6A) and 206C(7), Income-tax Act, 1961";
        var source = request.Regime == TcsBuyerReturnReliefRegime.IncomeTaxAct2025
            ? "Income-tax Act, 2025, Section 398(2) and Section 398(3)(c)"
            : "Income-tax Act, 1961, Section 206C(6A) first proviso and Section 206C(7) first proviso";

        var findings = new List<string>();
        if (request.AssessingOfficerOrderExists)
        {
            findings.Add("An Assessing Officer order exists; interest must be reviewed and applied in accordance with that order.");
            return Result(TcsBuyerReturnReliefStatus.ReviewRequired, false, null, section, findings, source);
        }

        if (!IsCoveredCategory(request.Regime, request.CollectionCategory))
            findings.Add("The collection provision is outside the buyer-return relief categories expressly covered by the selected Act.");
        if (request.BuyerReturnFiledDate is null)
            findings.Add("Buyer return filing date is missing.");
        else if (request.BuyerReturnFiledDate.Value < request.CollectibleDate)
            throw new ArgumentException("Buyer return filing date cannot be earlier than the TCS collectible date.", nameof(request));
        if (!request.BuyerIncludedAmountInReturn)
            findings.Add("The buyer has not confirmed that the transaction amount was included in the income computation in the return.");
        if (!request.BuyerPaidTaxDueOnDeclaredIncome)
            findings.Add("The buyer has not confirmed payment of tax due on the income declared in the return.");
        if (!request.AccountantCertificateAvailable)
            findings.Add(request.Regime == TcsBuyerReturnReliefRegime.IncomeTaxAct1961FromApril2025
                ? "The prescribed accountant certificate (Form 27BA) is not available."
                : "The prescribed accountant certificate is not available.");

        if (findings.Count > 0)
            return Result(TcsBuyerReturnReliefStatus.NotEligible, false, null, section, findings, source);

        findings.Add("All statutory buyer-return conditions and the accountant-certificate evidence are marked as satisfied; verify the underlying return, computation, tax payment and certificate before relying on this assessment.");
        findings.Add("Collector is not treated as an assessee in default for the qualifying failure to collect; 1% simple interest per month or part remains payable up to the buyer return filing date.");
        return Result(TcsBuyerReturnReliefStatus.EligibleForRelief, true,
            request.BuyerReturnFiledDate, section, findings, source);
    }

    private static bool IsCoveredCategory(TcsBuyerReturnReliefRegime regime, TcsBuyerReturnReliefCollectionCategory category)
    {
        // The 1961 Act first proviso expressly covers sections 206C(1) and 206C(1C).
        // The 2025 Act section 398(2)(b) covers section 394(1), Table Sl. Nos. 1-5 and 9.
        return category is TcsBuyerReturnReliefCollectionCategory.Section206C1SpecifiedGoodsOrNewActTable1To5
            or TcsBuyerReturnReliefCollectionCategory.Section206C1CLeaseOrNewActTable9;
    }

    private static void ValidateRegimeDate(TcsBuyerReturnReliefRequest request)
    {
        var valid = request.Regime switch
        {
            TcsBuyerReturnReliefRegime.IncomeTaxAct1961FromApril2025 =>
                request.CollectibleDate >= AmendmentStart && request.CollectibleDate < NewActStart,
            TcsBuyerReturnReliefRegime.IncomeTaxAct2025 =>
                request.CollectibleDate >= NewActStart,
            _ => false
        };
        if (!valid)
            throw new ArgumentException("Collectible date does not belong to the selected statutory regime. The buyer-return interest proviso is assessed only for the amended 1961 Act period from 1 April 2025 and the 2025 Act period from 1 April 2026.", nameof(request));
    }

    private static TcsBuyerReturnReliefAssessment Result(TcsBuyerReturnReliefStatus status,
        bool notDeemedInDefault, DateOnly? interestEndDate, string section,
        IReadOnlyList<string> findings, string source) =>
        new(status, notDeemedInDefault, interestEndDate, 1.0m, section, findings, source);
}
