namespace TallyAuditAssistant.Engine.Tds.Returns;

public enum TdsStatementKind
{
    Salary,
    NonSalaryResident,
    NonSalaryNonResident,
    TaxCollectedAtSource
}

public enum TdsLegislativeRegime
{
    IncomeTaxAct1961,
    IncomeTaxAct2025
}

public sealed record TdsStatementPeriod(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int Quarter,
    string YearLabel);

public sealed record TdsStatementLine(
    string DeducteePan,
    string SectionOrProvision,
    DateOnly CreditOrPaymentDate,
    decimal AmountPaidOrCredited,
    decimal TaxDeducted,
    decimal TaxDeposited,
    string? ChallanReference = null,
    string? DeducteeReference = null);

public sealed record TdsStatementChallan(
    string ChallanReference,
    DateOnly DepositDate,
    decimal Tax,
    decimal Surcharge = 0,
    decimal Cess = 0,
    decimal Interest = 0,
    decimal Fee = 0);

public sealed record TdsStatementIssue(
    string Code,
    string Message,
    int? LineNumber = null,
    string? Reference = null);

public sealed class TdsStatementWorkpaper
{
    public TdsStatementKind StatementKind { get; init; }
    public TdsLegislativeRegime Regime { get; init; }
    public string FormNumber { get; init; } = string.Empty;
    public string PeriodLabel { get; init; } = string.Empty;
    public DateOnly FilingDueDate { get; init; }
    public TdsStatementPeriod Period { get; init; } = new(default, default, 0, string.Empty);
    public int SourceLineCount { get; init; }
    public int AcceptedLineCount { get; init; }
    public decimal TotalAmountPaidOrCredited { get; init; }
    public decimal TotalTaxDeducted { get; init; }
    public decimal TotalTaxDepositedOnLines { get; init; }
    public decimal ChallanTaxTotal { get; init; }
    public decimal ChallanSurchargeTotal { get; init; }
    public decimal ChallanCessTotal { get; init; }
    public decimal ChallanInterestTotal { get; init; }
    public decimal ChallanFeeTotal { get; init; }
    public decimal NetTaxAndChargesDeposited => ChallanTaxTotal + ChallanSurchargeTotal +
        ChallanCessTotal + ChallanInterestTotal + ChallanFeeTotal;
    public IReadOnlyList<TdsStatementLine> Lines { get; init; } = Array.Empty<TdsStatementLine>();
    public IReadOnlyList<TdsStatementChallan> Challans { get; init; } = Array.Empty<TdsStatementChallan>();
    public IReadOnlyList<TdsStatementIssue> Issues { get; init; } = Array.Empty<TdsStatementIssue>();
    public bool IsReadyForReview => SourceLineCount == AcceptedLineCount && Issues.Count == 0;
}

/// <summary>Form mapping for regular quarterly statements, not correction statements or challan-cum-statements.</summary>
public sealed record TdsStatementFormMapping(
    TdsLegislativeRegime Regime,
    TdsStatementKind StatementKind,
    string FormNumber,
    string LegalPeriodDescription,
    string? DataStructureVersion);
