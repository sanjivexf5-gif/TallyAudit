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
    private static readonly Regex PanPattern = new("^[A-Z]{5}[0-9]{4}[A-Z]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public TdsStatementFormMapping ResolveForm(TdsStatementKind kind, DateOnly periodStart, DateOnly periodEnd)
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
            TdsStatementKind.Salary => periodStart >= new DateOnly(2026, 4, 1) ? "1.2 (Q1-Q3)" : null,
            TdsStatementKind.NonSalaryResident => "1.2",
            TdsStatementKind.NonSalaryNonResident => "1.2",
            TdsStatementKind.TaxCollectedAtSource => "1.2",
            _ => null
        };
        return new TdsStatementFormMapping(regime, kind, form,
            oldAct ? "Financial Year 2025-26 and earlier" : "Tax Year 2026-27 onward", version);
    }

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

        var mapping = ResolveForm(kind, periodStart, periodEnd);
        ValidateQuarter(periodStart, periodEnd, quarter);
        var lines = sourceLines.ToArray();
        var challans = (sourceChallans ?? Array.Empty<TdsStatementChallan>()).ToArray();
        var issues = new List<TdsStatementIssue>();

        if (string.IsNullOrWhiteSpace(deductorTan) || !Regex.IsMatch(deductorTan.Trim(), "^[A-Z]{4}[0-9]{5}[A-Z]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            issues.Add(new("INVALID_DEDUCTOR_TAN", "A structurally valid 10-character deductor TAN is required."));

        var accepted = new List<TdsStatementLine>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNo = index + 1;
            var valid = true;
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

        var period = new TdsStatementPeriod(periodStart, periodEnd, quarter, yearLabel.Trim());
        return new TdsStatementWorkpaper
        {
            StatementKind = kind,
            Regime = mapping.Regime,
            FormNumber = mapping.FormNumber,
            PeriodLabel = $"{yearLabel.Trim()} Q{quarter}",
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
