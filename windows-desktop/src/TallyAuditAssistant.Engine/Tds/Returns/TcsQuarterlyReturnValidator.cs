using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TallyAuditAssistant.Engine.Tds.Returns;

public sealed record TcsQuarterlyReturnLine(
    string TransactionReference,
    string BuyerPan,
    string Section,
    DateOnly CollectionDate,
    decimal SaleAmount,
    decimal TcsAmount);

public sealed record TcsQuarterlyReturn(
    string Tan,
    string Form,
    int FinancialYearStartYear,
    int Quarter,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly? FiledOn,
    IReadOnlyList<TcsQuarterlyReturnLine> Lines);

public sealed record TcsReturnValidationFinding(
    string Code,
    string Severity,
    string Message,
    string AuditorAction,
    string? TransactionReference = null);

/// <summary>
/// Performs structural and period-level pre-filing checks for quarterly TCS statements.
/// It does not calculate statutory TCS rates, certify data, upload files or file returns.
/// </summary>
public sealed class TcsQuarterlyReturnValidator
{
    private static readonly DateOnly NewActStart = new(2026, 4, 1);
    private static readonly Regex TanPattern = new("^[A-Z]{4}[0-9]{5}[A-Z]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PanPattern = new("^[A-Z]{5}[0-9]{4}[A-Z]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly TcsComplianceDeadlineService _deadlines;

    public TcsQuarterlyReturnValidator(TcsComplianceDeadlineService? deadlines = null)
        => _deadlines = deadlines ?? new TcsComplianceDeadlineService();

    public IReadOnlyList<TcsReturnValidationFinding> Validate(TcsQuarterlyReturn statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(statement.Lines);

        var findings = new List<TcsReturnValidationFinding>();
        if (statement.FinancialYearStartYear is < 1900 or > 9998 || statement.Quarter is < 1 or > 4)
        {
            Add(findings, "INVALID_PERIOD", "Error", "Financial year or quarter is invalid.",
                "Select a valid Indian financial year and quarter (1 to 4).");
            return findings;
        }

        var expectedStart = QuarterStart(statement.FinancialYearStartYear, statement.Quarter);
        var expectedEnd = expectedStart.AddMonths(3).AddDays(-1);
        if (statement.PeriodStart != expectedStart || statement.PeriodEnd != expectedEnd)
            Add(findings, "PERIOD_MISMATCH", "Error", "Statement dates do not match the selected financial-year quarter.",
                $"Use {expectedStart:dd MMM yyyy} to {expectedEnd:dd MMM yyyy} for Q{statement.Quarter}.");

        var governingAct = expectedEnd < NewActStart ? TcsComplianceAct.IncomeTaxAct1961 : TcsComplianceAct.IncomeTaxAct2025;
        var expectedForm = governingAct == TcsComplianceAct.IncomeTaxAct1961 ? "FORM 27EQ" : "FORM 143";
        if (!string.Equals(Normalize(statement.Form), expectedForm, StringComparison.Ordinal))
            Add(findings, "FORM_ACT_MISMATCH", "Error",
                $"The selected period requires {expectedForm} under the {ActName(governingAct)}.",
                $"Prepare {expectedForm} for this quarter; do not use the form applicable to the other Act.");

        if (!TanPattern.IsMatch(Normalize(statement.Tan)))
            Add(findings, "INVALID_TAN", "Error", "Collector TAN is missing or structurally invalid.",
                "Verify the TAN against the collector's authoritative tax registration.");

        var deadline = _deadlines.GetQuarterlyDeadlines(statement.FinancialYearStartYear)
            .Single(x => x.Code == "TCS-STATEMENT" && x.PeriodStart == expectedStart);
        if (statement.FiledOn is { } filedOn && filedOn > deadline.DueDate)
            Add(findings, "LATE_FILING", "Warning",
                $"Statement filing date {filedOn:dd MMM yyyy} is after the standard due date {deadline.DueDate:dd MMM yyyy}.",
                "Check any CBDT extension or portal-specific relief and retain supporting evidence.");

        var duplicateReferenceGroups = statement.Lines
            .Where(x => !string.IsNullOrWhiteSpace(x.TransactionReference))
            .GroupBy(x => Normalize(x.TransactionReference), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToArray();
        var duplicateReferences = duplicateReferenceGroups
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
        var conflictingReferences = duplicateReferenceGroups
            .Where(group => group
                .Select(line => (
                    BuyerPan: Normalize(line.BuyerPan),
                    Section: Normalize(line.Section),
                    line.CollectionDate,
                    line.SaleAmount,
                    line.TcsAmount))
                .Distinct()
                .Skip(1)
                .Any())
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var line in statement.Lines)
        {
            var reference = string.IsNullOrWhiteSpace(line.TransactionReference) ? null : line.TransactionReference.Trim();
            if (reference is null)
                Add(findings, "MISSING_TRANSACTION_REFERENCE", "Error", "A statement line has no transaction reference.",
                    "Link the collection entry to its source voucher or invoice.", reference);
            else if (duplicateReferences.Contains(Normalize(reference)))
            {
                Add(findings, "DUPLICATE_TRANSACTION_REFERENCE", "Warning",
                    "The transaction reference occurs more than once in this statement.",
                    "Check whether the repeated rows are genuine separate collections or duplicate imports.", reference);
                if (conflictingReferences.Contains(Normalize(reference)))
                    Add(findings, "CONFLICTING_TRANSACTION_REFERENCE", "Error",
                        "Rows with the same transaction reference contain conflicting buyer, section, collection date or amount details.",
                        "Trace the reference to the source voucher or invoice, correct the source mapping, and retain evidence for any legitimate split collection.", reference);
            }

            if (!PanPattern.IsMatch(Normalize(line.BuyerPan)))
                Add(findings, "INVALID_BUYER_PAN", "Error", "Buyer PAN is missing or structurally invalid.",
                    "Verify buyer identity and PAN; do not infer or auto-correct a PAN.", reference);

            if (string.IsNullOrWhiteSpace(line.Section))
                Add(findings, "MISSING_SECTION", "Error", "TCS section is missing on a statement line.",
                    "Map the transaction to the correct statutory TCS section.", reference);

            if (line.CollectionDate < statement.PeriodStart || line.CollectionDate > statement.PeriodEnd)
                Add(findings, "COLLECTION_OUTSIDE_PERIOD", "Error",
                    "Collection date falls outside the statement quarter.",
                    "Confirm the collection date and allocate the transaction to the correct quarter.", reference);

            if (line.SaleAmount <= 0m || line.TcsAmount < 0m)
                Add(findings, "INVALID_AMOUNT", "Error", "Sale amount must be positive and TCS amount cannot be negative.",
                    "Reconcile the source invoice and TCS ledger amount.", reference);

            if (line.TcsAmount > line.SaleAmount)
                Add(findings, "TCS_EXCEEDS_SALE", "Error", "Reported TCS exceeds the reported sale amount.",
                    "Check amount fields, units and source mapping before preparing the statement.", reference);
        }

        if (statement.Lines.Count == 0)
            Add(findings, "EMPTY_STATEMENT", "Warning", "No collection lines were supplied for this quarterly statement.",
                "Confirm whether the collector had no reportable collections or the import is incomplete.");

        return findings;
    }

    private static DateOnly QuarterStart(int financialYearStartYear, int quarter) => quarter switch
    {
        1 => new DateOnly(financialYearStartYear, 4, 1),
        2 => new DateOnly(financialYearStartYear, 7, 1),
        3 => new DateOnly(financialYearStartYear, 10, 1),
        4 => new DateOnly(financialYearStartYear + 1, 1, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(quarter))
    };

    private static string ActName(TcsComplianceAct act) => act == TcsComplianceAct.IncomeTaxAct1961
        ? "Income-tax Act, 1961" : "Income-tax Act, 2025";

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().Trim('\uFEFF').ToUpperInvariant();

    private static void Add(ICollection<TcsReturnValidationFinding> findings, string code, string severity,
        string message, string action, string? reference = null)
        => findings.Add(new TcsReturnValidationFinding(code, severity, message, action, reference));
}
