namespace TallyAuditAssistant.Engine.Tds.Calculation;

public enum TdsDeducteeEntityType
{
    IndividualOrHuf,
    Other
}

public enum TdsCalculationStatus
{
    Calculated,
    BelowThreshold,
    ReviewRequired
}

public sealed record TdsDeductionRequest(
    DateOnly? CreditDate,
    DateOnly? PaymentDate,
    decimal AmountPaidOrCredited,
    decimal PriorAggregatePaidOrCreditedInFinancialYear,
    TdsDeducteeEntityType DeducteeEntityType,
    bool PanProvided,
    bool ApplicabilityConfirmed,
    decimal? LowerDeductionCertificateRatePercent = null,
    string? LowerDeductionCertificateReference = null,
    DateOnly? CertificateValidFrom = null,
    DateOnly? CertificateValidTo = null);

public sealed record TdsDeductionResult(
    TdsCalculationStatus Status,
    string GoverningAct,
    string SectionReference,
    decimal? AppliedRatePercent,
    decimal TaxableAmount,
    decimal TdsAmount,
    string Explanation,
    string SourceReference);
