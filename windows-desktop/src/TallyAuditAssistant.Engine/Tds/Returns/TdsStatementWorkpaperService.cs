using System.Globalization;
using System.Text.RegularExpressions;

namespace TallyAuditAssistant.Engine.Tds.Returns;

/// <summary>
/// Builds a transition-aware quarterly TDS/TCS review workpaper.
/// It deliberately does not generate RPU/FVU files, compute statutory rates, or file statements.
/// </summary>
public sealed class TdsStatementWorkpaperService
{
    private static readonly DateOnly TransitionDate = new(2026, 4, 1);
    private static readonly Regex PanPattern = new("^[A-Z]{3}[ABCFGHLJPT][A-Z][0-9]{4}[A-Z]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public TdsStatementFormMapping ResolveForm(TdsStatementKind kind, DateOnly periodStart, DateOnly periodEnd, int? quarter = null)
    {
        if (periodStart > periodEnd) throw new ArgumentException("Statement period start must not exceed its end.");
        if (periodStart < TransitionDate && periodEnd >= TransitionDate)
            throw new ArgumentException("A quarterly statement cannot span the 1 April 2026 legislative transition. Prepare separate old-Act and new-Act statements.");

        var oldAct = periodEnd < TransitionDate;
        var regime = oldAct ? TdsLegislativeRegime.IncomeTaxAct1961 : TdsLegislativeRegime.IncomeTaxAct2025;
        var form = kind switch
        {
            TdsStatementKind.Salary => oldAct ? "24Q" : "138",
            TdsStatementKind.NonSalaryResident => oldAct ? "26Q" : "140",
            TdsStatementKind.NonSalaryNonResident => oldAct ? "27Q" : "144",
            TdsStatementKind.TaxCollectedAtSource => oldAct ? "27EQ" : "143",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var version = oldAct ? null : kind switch
        {
            TdsStatementKind.Salary => quarter is >= 1 and <= 3 ? "1.2 (Q1-Q3)" : quarter == 4 ? "1.0 (Q4; verify current release)" : null,
            TdsStatementKind.NonSalaryResident => "1.2",
            TdsStatementKind.NonSalaryNonResident => "1.2",
            TdsStatementKind.TaxCollectedAtSource => "1.2",
            _ => null
        };
        return new TdsStatementFormMapping(regime, kind, form,
            oldAct ? "Financial Year 2025-26 and earlier" : "Tax Year 2026-27 onward", version);
    }

    /// <summary>Returns the statutory quarterly statement due date for a quarter-end date.
    /// Any later CBDT extension must be applied separately and retained as evidence.</summary>
    public DateOnly GetFilingDueDate(DateOnly periodEnd) => periodEnd switch
    {
        { Month: 6, Day: 30 } => new DateOnly(periodEnd.Year, 7, 31),
        { Month: 9, Day: 30 } => new DateOnly(periodEnd.Year, 10, 31),
        { Month: 12, Day: 31 } => new DateOnly(periodEnd.Year + 1, 1, 31),
        { Month: 3, Day: 31 } => new DateOnly(periodEnd.Year, 5, 31),
        _ => throw new ArgumentException("Filing due date can only be calculated for a statutory quarter-end (30 June, 30 September, 31 December or 31 March).", nameof(periodEnd))
    };

    public TdsStatementWorkpaper Prepare(
        TdsStatementKind kind,
        DateOnly periodStart,
        DateOnly periodEnd,
        string yearLabel,
        int quarter,
        string? deductorTan,
        IEnumerable<TdsStatementLine> sourceLines,
        IEnumerable<TdsStatementChallan>? sourceChallans = null)
    {
        ArgumentNullException.ThrowIfNull(sourceLines);
        if (quarter is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(quarter), "Quarter must be 1, 2, 3 or 4.");
        if (string.IsNullOrWhiteSpace(yearLabel)) throw new ArgumentException("Financial year / tax year label is required.", nameof(yearLabel));
        if (periodStart > periodEnd) throw new ArgumentException("Statement period start must not exceed its end.");

        var mapping = ResolveForm(kind, periodStart, periodEnd, quarter);
        ValidateQuarter(periodStart, periodEnd, quarter);
        var lines = sourceLines.ToArray();
        var challans = (sourceChallans ?? Array.Empty<TdsStatementChallan>()).ToArray();
        var issues = new List<TdsStatementIssue>();

        if (string.IsNullOrWhiteSpace(deductorTan) || !Regex.IsMatch(deductorTan.Trim(), "^[A-Z]{4}[0-9]{5}[A-Z]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            issues.Add(new("INVALID_DEDUCTOR_TAN", "A structurally valid 10-character deductor TAN is required."));

        // A repeated source reference makes the deductee line identity ambiguous.
        // Exclude every row carrying that reference rather than silently keeping the first.
        var duplicateDeducteeReferences = lines
            .Where(x => !string.IsNullOrWhiteSpace(x.DeducteeReference))
            .GroupBy(x => x.DeducteeReference!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in duplicateDeducteeReferences)
            issues.Add(new("DUPLICATE_DEDUCTEE_REFERENCE",
                "The same source deductee reference appears more than once. All rows with this reference are excluded until the source is corrected.",
                Reference: reference));

        var accepted = new List<TdsStatementLine>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNo = index + 1;
            var valid = string.IsNullOrWhiteSpace(line.DeducteeReference) ||
                !duplicateDeducteeReferences.Contains(line.DeducteeReference.Trim());
            void Add(string code, string message) { issues.Add(new(code, message, lineNo, line.DeducteeReference)); valid = false; }

            if (string.IsNullOrWhiteSpace(line.DeducteePan) || !PanPattern.IsMatch(line.DeducteePan.Trim().ToUpperInvariant()))
                Add("INVALID_DEDUCTEE_PAN", "Deductee PAN is missing or does not match the standard PAN structure. Confirm any legally permitted PAN exception separately.");
            if (string.IsNullOrWhiteSpace(line.SectionOrProvision))
                Add("MISSING_SECTION", "Applicable section / provision reference is required for each line.");
            if (line.CreditOrPaymentDate < periodStart || line.CreditOrPaymentDate > periodEnd)
                Add("DATE_OUTSIDE_PERIOD", "Credit or payment date falls outside the selected quarterly statement period.");
            if (line.AmountPaidOrCredited < 0)
                Add("NEGATIVE_PAYMENT_AMOUNT", "Amount paid or credited cannot be negative.");
            if (line.TaxDeducted < 0 || line.TaxDeposited < 0)
                Add("NEGATIVE_TAX_AMOUNT", "Tax deducted and tax deposited cannot be negative.");
            if (line.TaxDeducted > line.AmountPaidOrCredited)
                Add("TDS_EXCEEDS_PAYMENT", "Tax deducted exceeds the underlying amount paid or credited; verify gross-up or data mapping.");
            if (line.TaxDeposited > line.TaxDeducted)
                Add("DEPOSIT_EXCEEDS_DEDUCTION", "Tax deposited allocated to this line exceeds tax deducted; reconcile challan allocation.");
            if (valid) accepted.Add(line);
        }

        var acceptedChallans = new List<TdsStatementChallan>();
        foreach (var challan in challans)
        {
            if (string.IsNullOrWhiteSpace(challan.ChallanReference))
            { issues.Add(new("MISSING_CHALLAN_REFERENCE", "Each challan requires a traceable reference.")); continue; }
            if (challan.DepositDate == DateOnly.MinValue)
            { issues.Add(new("INVALID_CHALLAN_DATE", "Each challan requires a valid deposit date.", Reference: challan.ChallanReference)); continue; }
            if (challan.DepositDate > periodEnd.AddDays(90))
                issues.Add(new("CHALLAN_DATE_REVIEW", "Challan deposit date is unusually later than the statement period; verify timing and allocation.", Reference: challan.ChallanReference));
            if (challan.Tax < 0 || challan.Surcharge < 0 || challan.Cess < 0 || challan.Interest < 0 || challan.Fee < 0)
            { issues.Add(new("NEGATIVE_CHALLAN_COMPONENT", "Challan tax, surcharge, cess, interest and fee components cannot be negative.", Reference: challan.ChallanReference)); continue; }
            acceptedChallans.Add(challan);
        }

        var duplicateChallans = acceptedChallans.GroupBy(x => x.ChallanReference.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);
        foreach (var duplicate in duplicateChallans)
            issues.Add(new("DUPLICATE_CHALLAN_REFERENCE", "The same challan reference appears more than once.", Reference: duplicate.Key));

        // Match deductee allocations to the challan pool. TRACES treats excess
        // consumption as an overbooked challan; missing or unmatched references
        // must be reviewed before a statement is considered ready.
        var challanGroups = acceptedChallans
            .GroupBy(x => x.ChallanReference.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
        var allocations = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in accepted.Where(x => x.TaxDeposited > 0))
        {
            if (string.IsNullOrWhiteSpace(line.ChallanReference))
            {
                issues.Add(new("MISSING_LINE_CHALLAN_REFERENCE",
                    "Tax deposited is allocated to a deductee line without a challan reference.",
                    Reference: line.DeducteeReference));
                continue;
            }

            var reference = line.ChallanReference.Trim();
            if (!challanGroups.TryGetValue(reference, out var matchingChallans) || matchingChallans.Length != 1)
            {
                issues.Add(new("UNMATCHED_LINE_CHALLAN_REFERENCE",
                    "The deductee line refers to a challan that is missing or ambiguous in the supplied challan records.",
                    Reference: reference));
                continue;
            }

            allocations[reference] = allocations.GetValueOrDefault(reference) + line.TaxDeposited;
        }

        foreach (var allocation in allocations)
        {
            var challan = challanGroups[allocation.Key][0];
            if (allocation.Value > challan.Tax)
                issues.Add(new("OVERBOOKED_CHALLAN_ALLOCATION",
                    $"Deductee allocations ({allocation.Value.ToString(CultureInfo.InvariantCulture)}) exceed challan tax ({challan.Tax.ToString(CultureInfo.InvariantCulture)}).",
                    Reference: allocation.Key));
        }

        var period = new TdsStatementPeriod(periodStart, periodEnd, quarter, yearLabel.Trim());
        return new TdsStatementWorkpaper
        {
            StatementKind = kind,
            Regime = mapping.Regime,
            FormNumber = mapping.FormNumber,
            PeriodLabel = $"{yearLabel.Trim()} Q{quarter}",
            FilingDueDate = GetFilingDueDate(periodEnd),
            Period = period,
            SourceLineCount = lines.Length,
            AcceptedLineCount = accepted.Count,
            TotalAmountPaidOrCredited = accepted.Sum(x => x.AmountPaidOrCredited),
            TotalTaxDeducted = accepted.Sum(x => x.TaxDeducted),
            TotalTaxDepositedOnLines = accepted.Sum(x => x.TaxDeposited),
            ChallanTaxTotal = acceptedChallans.Sum(x => x.Tax),
            ChallanSurchargeTotal = acceptedChallans.Sum(x => x.Surcharge),
            ChallanCessTotal = acceptedChallans.Sum(x => x.Cess),
            ChallanInterestTotal = acceptedChallans.Sum(x => x.Interest),
            ChallanFeeTotal = acceptedChallans.Sum(x => x.Fee),
            Lines = accepted.ToArray(),
            Challans = acceptedChallans.ToArray(),
            Issues = issues
        };
    }

    private static void ValidateQuarter(DateOnly start, DateOnly end, int quarter)
    {
        var financialYearStart = start.Month >= 4
            ? new DateOnly(start.Year, 4, 1)
            : new DateOnly(start.Year - 1, 4, 1);
        var expectedStart = financialYearStart.AddMonths((quarter - 1) * 3);
        var expectedEnd = expectedStart.AddMonths(3).AddDays(-1);
        if (start != expectedStart || end != expectedEnd)
            throw new ArgumentException($"Quarter {quarter} must cover {expectedStart:yyyy-MM-dd} through {expectedEnd:yyyy-MM-dd}.");
    }
}
