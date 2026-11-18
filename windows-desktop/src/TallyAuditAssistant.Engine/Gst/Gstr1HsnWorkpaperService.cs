namespace TallyAuditAssistant.Engine.Gst;

/// <summary>
/// Prepares the HSN/SAC summary for GSTR-1 Table 12. HSN classification and UQC selection
/// remain the responsibility of a reviewer; this service validates supplied data only.
/// </summary>
public sealed class Gstr1HsnWorkpaperService
{
    private const decimal TaxRoundingTolerance = 1.00m;

    public Gstr1HsnWorkpaper Prepare(DateTime returnPeriodStart, DateTime returnPeriodEnd,
        decimal previousFinancialYearAggregateTurnover, IEnumerable<Gstr1HsnLine> sourceLines)
    {
        if (returnPeriodStart.Date > returnPeriodEnd.Date)
            throw new ArgumentException("Return period start must not exceed return period end.");
        if (previousFinancialYearAggregateTurnover < 0)
            throw new ArgumentOutOfRangeException(nameof(previousFinancialYearAggregateTurnover));
        ArgumentNullException.ThrowIfNull(sourceLines);

        var rows = sourceLines.ToList();
        var issues = new List<Gstr1HsnIssue>();
        var accepted = new List<Gstr1HsnLine>();
        var minimumDigits = previousFinancialYearAggregateTurnover > 5_00_00_000m ? 6 : 4;

        foreach (var row in rows)
        {
            void Issue(string code, string message) => issues.Add(new(row.SourceId, code, message));
            if (string.IsNullOrWhiteSpace(row.SourceId))
            { Issue("MISSING_SOURCE_ID", "A source identifier is required for audit traceability."); continue; }
            if (!Enum.IsDefined(row.SupplyTab))
            { Issue("INVALID_SUPPLY_TAB", "Select the B2B or B2C HSN summary tab."); continue; }
            var code = row.HsnCode?.Trim() ?? string.Empty;
            if (code.Length < minimumDigits || code.Length > 8 || code.Length % 2 != 0 || !code.All(char.IsDigit))
            { Issue("INVALID_HSN_LENGTH", $"HSN/SAC code must contain {minimumDigits} to 8 numeric digits, using 2-digit increments, based on preceding-FY aggregate turnover."); continue; }
            if (string.IsNullOrWhiteSpace(row.Description))
            { Issue("MISSING_HSN_DESCRIPTION", "HSN/SAC description is required."); continue; }
            if (string.IsNullOrWhiteSpace(row.Uqc))
            { Issue("MISSING_UQC", "Unit quantity code (UQC) is required."); continue; }
            if (row.Quantity < 0 || row.TaxableValue < 0 || row.Igst < 0 || row.Cgst < 0 || row.Sgst < 0 || row.Cess < 0)
            { Issue("NEGATIVE_AMOUNT", "Quantity and tax values cannot be negative in the HSN summary."); continue; }
            if (row.TaxRate < 0 || row.TaxRate > 100 || decimal.Round(row.TaxRate, 2) != row.TaxRate)
            { Issue("INVALID_TAX_RATE", "Tax rate must be between 0 and 100 with at most two decimal places."); continue; }
            if (row.Igst > 0 && (row.Cgst > 0 || row.Sgst > 0))
            { Issue("MIXED_TAX_HEADS", "IGST and CGST/SGST cannot both be populated on the same HSN summary line."); continue; }
            if (row.Cgst > 0 && Math.Abs(row.Cgst - row.Sgst) > TaxRoundingTolerance)
            { Issue("INTRA_STATE_SPLIT", "CGST and SGST differ by more than ₹1.00."); continue; }
            var calculatedTax = decimal.Round(row.TaxableValue * row.TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(calculatedTax - row.Igst - row.Cgst - row.Sgst) > TaxRoundingTolerance)
            { Issue("TAX_AMOUNT_MISMATCH", "IGST plus CGST plus SGST differs from rate-derived tax by more than ₹1.00; cess is separate."); continue; }
            accepted.Add(row with { HsnCode = code, Uqc = row.Uqc.Trim(), Description = row.Description.Trim() });
        }

        var summaries = accepted
            .GroupBy(x => new { x.SupplyTab, x.HsnCode, x.Description, x.Uqc, x.TaxRate })
            .OrderBy(x => x.Key.SupplyTab).ThenBy(x => x.Key.HsnCode, StringComparer.Ordinal)
            .ThenBy(x => x.Key.TaxRate)
            .Select(g => new Gstr1HsnSummary(g.Key.SupplyTab, g.Key.HsnCode, g.Key.Description, g.Key.Uqc,
                g.Key.TaxRate, g.Count(), decimal.Round(g.Sum(x => x.Quantity), 3),
                decimal.Round(g.Sum(x => x.TaxableValue), 2), decimal.Round(g.Sum(x => x.Igst), 2),
                decimal.Round(g.Sum(x => x.Cgst), 2), decimal.Round(g.Sum(x => x.Sgst), 2),
                decimal.Round(g.Sum(x => x.Cess), 2)))
            .ToArray();

        return new Gstr1HsnWorkpaper
        {
            ReturnPeriodStart = returnPeriodStart.Date,
            ReturnPeriodEnd = returnPeriodEnd.Date,
            PreviousFinancialYearAggregateTurnover = previousFinancialYearAggregateTurnover,
            SourceLineCount = rows.Count,
            IncludedLineCount = accepted.Count,
            Summaries = summaries,
            Issues = issues
        };
    }
}
