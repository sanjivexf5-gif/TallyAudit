namespace TallyAuditAssistant.Engine.Gst;

/// <summary>
/// Prepares a traceable GSTR-3B workpaper from explicitly classified source rows.
/// It does not decide legal eligibility, cross-utilisation, cash/credit payment, interest,
/// late fee, or file/submission to GSTN.
/// </summary>
public sealed class Gstr3BWorkpaperService
{
    public Gstr3BWorkpaper Prepare(DateTime fromDate, DateTime toDate,
        IEnumerable<Gstr3BLiabilityLine> liabilities,
        IEnumerable<Gstr3BItcLine> itcLines,
        IEnumerable<Gstr3BItcReversal> reversals)
    {
        if (fromDate.Date > toDate.Date) throw new ArgumentException("From date must not exceed to date.");
        ArgumentNullException.ThrowIfNull(liabilities);
        ArgumentNullException.ThrowIfNull(itcLines);
        ArgumentNullException.ThrowIfNull(reversals);

        var liabilityRows = liabilities.ToList();
        var itcRows = itcLines.ToList();
        var reversalRows = reversals.ToList();
        var issues = new List<Gstr3BValidationIssue>();
        var liabilityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);\n        var itcIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);\n        var reversalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool Validate(string sourceId, DateTime date, decimal taxable, Gstr3BTaxHeadSummary tax, string sourceType, HashSet<string> seenIds)
        {
            void Issue(string code, string message) => issues.Add(new(sourceId ?? string.Empty, code, message));
            if (string.IsNullOrWhiteSpace(sourceId))
            { Issue("MISSING_SOURCE_ID", $"{sourceType} source ID is required."); return false; }
            if (!seenIds.Add(sourceId.Trim()))
            { Issue("DUPLICATE_SOURCE_ID", "Source ID is duplicated across the GSTR-3B workpaper inputs."); return false; }
            if (date.Date < fromDate.Date || date.Date > toDate.Date)
            { Issue("OUTSIDE_PERIOD", "Source document date is outside the selected return period."); return false; }
            if (taxable < 0 || tax.Igst < 0 || tax.Cgst < 0 || tax.Sgst < 0 || tax.Cess < 0)
            { Issue("NEGATIVE_AMOUNT", "Negative values are not accepted in this workpaper line; classify amendments/reversals separately."); return false; }
            return true;
        }

        var acceptedLiabilities = new List<Gstr3BLiabilityLine>();
        foreach (var row in liabilityRows)
        {
            var tax = new Gstr3BTaxHeadSummary(row.Igst, row.Cgst, row.Sgst, row.Cess);
            if (!Enum.IsDefined(row.Section))
            { issues.Add(new(row.SourceId ?? string.Empty, "INVALID_LIABILITY_SECTION", "Liability section is not recognised.")); continue; }
            if (Validate(row.SourceId, row.DocumentDate, row.TaxableValue, tax, "Liability", liabilityIds))
                acceptedLiabilities.Add(row);
        }

        var acceptedItc = new List<Gstr3BItcLine>();
        foreach (var row in itcRows)
        {
            var tax = new Gstr3BTaxHeadSummary(row.Igst, row.Cgst, row.Sgst, row.Cess);
            if (!Enum.IsDefined(row.Category))
            { issues.Add(new(row.SourceId ?? string.Empty, "INVALID_ITC_CATEGORY", "ITC category is not recognised.")); continue; }
            if (Validate(row.SourceId, row.DocumentDate, 0, tax, "ITC", itcIds))
                acceptedItc.Add(row);
        }

        var acceptedReversals = new List<Gstr3BItcReversal>();
        foreach (var row in reversalRows)
        {
            var tax = new Gstr3BTaxHeadSummary(row.Igst, row.Cgst, row.Sgst, row.Cess);
            if (!Enum.IsDefined(row.Category))
            { issues.Add(new(row.SourceId ?? string.Empty, "INVALID_REVERSAL_CATEGORY", "ITC reversal category is not recognised.")); continue; }
            if (Validate(row.SourceId, row.DocumentDate, 0, tax, "ITC reversal", reversalIds))
                acceptedReversals.Add(row);
        }

        var liabilitySummary = acceptedLiabilities.GroupBy(x => x.Section).OrderBy(x => x.Key)
            .Select(g => new Gstr3BLiabilitySummary(g.Key, g.Count(), decimal.Round(g.Sum(x => x.TaxableValue), 2),
                SumTax(g.Select(x => new Gstr3BTaxHeadSummary(x.Igst, x.Cgst, x.Sgst, x.Cess))))).ToArray();
        var itcSummary = acceptedItc.GroupBy(x => x.Category).OrderBy(x => x.Key)
            .Select(g => new Gstr3BItcSummary(g.Key, g.Count(),
                SumTax(g.Select(x => new Gstr3BTaxHeadSummary(x.Igst, x.Cgst, x.Sgst, x.Cess))))).ToArray();
        var reversalSummary = acceptedReversals.GroupBy(x => x.Category).OrderBy(x => x.Key)
            .Select(g => new Gstr3BReversalSummary(g.Key, g.Count(),
                SumTax(g.Select(x => new Gstr3BTaxHeadSummary(x.Igst, x.Cgst, x.Sgst, x.Cess))))).ToArray();

        var available = SumTax(acceptedItc.Select(x => new Gstr3BTaxHeadSummary(x.Igst, x.Cgst, x.Sgst, x.Cess)));
        var reversed = SumTax(acceptedReversals.Select(x => new Gstr3BTaxHeadSummary(x.Igst, x.Cgst, x.Sgst, x.Cess)));
        if (reversed.Igst > available.Igst || reversed.Cgst > available.Cgst ||
            reversed.Sgst > available.Sgst || reversed.Cess > available.Cess)
            issues.Add(new("WORKPAPER", "REVERSAL_EXCEEDS_ITC",
                "One or more tax-head reversals exceed available ITC. Verify prior-period balances and source classification; net ITC may be negative."));

        return new Gstr3BWorkpaper
        {
            FromDate = fromDate.Date, ToDate = toDate.Date,
            LiabilitySourceCount = liabilityRows.Count, ItcSourceCount = itcRows.Count, ReversalSourceCount = reversalRows.Count,
            IncludedLiabilityCount = acceptedLiabilities.Count, IncludedItcCount = acceptedItc.Count,
            IncludedReversalCount = acceptedReversals.Count,
            Liabilities = liabilitySummary, ItcAvailable = itcSummary, ItcReversals = reversalSummary, Issues = issues
        };
    }

    private static Gstr3BTaxHeadSummary SumTax(IEnumerable<Gstr3BTaxHeadSummary> rows) =>
        rows.Aggregate(new Gstr3BTaxHeadSummary(0, 0, 0, 0), (sum, row) => sum + row);
}
