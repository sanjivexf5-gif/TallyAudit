using System.Globalization;
using System.Text.RegularExpressions;

namespace TallyAuditAssistant.Engine.Gst;

/// <summary>
/// Validates and totals the GSTR-1 Table 13 document-series workpaper.
/// It does not generate GSTN payloads or file returns.
/// </summary>
public sealed class Gstr1DocumentsIssuedWorkpaperService
{
    private static readonly Regex NumericSuffix = new("^(.*?)([0-9]+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Gstr1DocumentsIssuedWorkpaper Prepare(
        DateTime returnPeriodStart,
        DateTime returnPeriodEnd,
        IEnumerable<Gstr1DocumentSeries> sourceSeries,
        Gstr1Table13Applicability? applicability = null)
    {
        if (returnPeriodStart.Date > returnPeriodEnd.Date)
            throw new ArgumentException("Return period start must not exceed return period end.");
        ArgumentNullException.ThrowIfNull(sourceSeries);

        var rows = sourceSeries.ToList();
        var issues = new List<Gstr1DocumentsIssuedIssue>();
        var accepted = new List<Gstr1DocumentSeries>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            void Issue(string code, string message) =>
                issues.Add(new(row.DocumentType, row.SerialNumberFrom, row.SerialNumberTo, code, message));

            if (!Enum.IsDefined(row.DocumentType))
            { Issue("INVALID_DOCUMENT_TYPE", "Select one of the twelve GSTR-1 Table 13 document categories."); continue; }
            var from = row.SerialNumberFrom?.Trim() ?? string.Empty;
            var to = row.SerialNumberTo?.Trim() ?? string.Empty;
            if (from.Length == 0 || to.Length == 0)
            { Issue("MISSING_SERIAL_RANGE", "Both From and To serial numbers are required."); continue; }
            if (row.TotalDocuments <= 0)
            { Issue("INVALID_TOTAL_DOCUMENTS", "Total number of documents must be greater than zero."); continue; }
            if (row.CancelledDocuments < 0 || row.CancelledDocuments > row.TotalDocuments)
            { Issue("INVALID_CANCELLED_COUNT", "Cancelled documents must be between zero and total documents."); continue; }

            if (TryGetNumericRange(from, to, out var range))
            {
                if (range.End < range.Start)
                { Issue("REVERSED_SERIAL_RANGE", "To serial number must not precede From serial number."); continue; }
                var capacity = range.End - range.Start + 1;
                if (row.TotalDocuments > capacity)
                { Issue("TOTAL_EXCEEDS_SERIAL_RANGE", "Total documents cannot exceed the number of serials in the stated range."); continue; }
            }
            else if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) && row.TotalDocuments > 1)
            { Issue("TOTAL_EXCEEDS_SERIAL_RANGE", "A single serial number cannot represent more than one document."); continue; }

            var key = $"{row.DocumentType}|{from}|{to}";
            if (!seen.Add(key))
            { Issue("DUPLICATE_DOCUMENT_SERIES", "The same document category and serial range has been entered more than once."); continue; }

            var overlap = accepted.FirstOrDefault(existing =>
                existing.DocumentType == row.DocumentType &&
                RangesOverlap(existing.SerialNumberFrom, existing.SerialNumberTo, from, to));
            if (overlap is not null)
            { Issue("OVERLAPPING_SERIAL_RANGE", "This serial range overlaps another range already entered for the same document category."); continue; }

            accepted.Add(row with { SerialNumberFrom = from, SerialNumberTo = to,
                EvidenceReference = string.IsNullOrWhiteSpace(row.EvidenceReference) ? null : row.EvidenceReference.Trim() });
        }

        var isPostMay2025 = returnPeriodStart.Date >= new DateTime(2025, 5, 1);
        var required = isPostMay2025 && (applicability?.RequiresDocumentsIssued ?? false);
        if (required && accepted.Count == 0)
            issues.Add(new(null, null, null, "TABLE13_REQUIRED",
                "From the May 2025 return period, Table 13 details are compulsory when data is reported in GSTR-1 Tables 4, 5, 6, 7, 8, 9, 10, 14 or 15."));

        return new Gstr1DocumentsIssuedWorkpaper
        {
            ReturnPeriodStart = returnPeriodStart.Date,
            ReturnPeriodEnd = returnPeriodEnd.Date,
            SourceSeriesCount = rows.Count,
            IncludedSeriesCount = accepted.Count,
            TotalDocuments = accepted.Sum(x => x.TotalDocuments),
            CancelledDocuments = accepted.Sum(x => x.CancelledDocuments),
            NetIssuedDocuments = accepted.Sum(x => x.NetIssuedDocuments),
            Series = accepted.ToArray(),
            Issues = issues,
            IsTableRequired = required
        };
    }

    private static bool TryGetNumericRange(string from, string to, out (long Start, long End) range)
    {
        range = default;
        var first = NumericSuffix.Match(from);
        var last = NumericSuffix.Match(to);
        if (!first.Success || !last.Success ||
            !string.Equals(first.Groups[1].Value, last.Groups[1].Value, StringComparison.OrdinalIgnoreCase) ||
            !long.TryParse(first.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
            !long.TryParse(last.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var end))
            return false;
        range = (start, end);
        return true;
    }

    private static bool RangesOverlap(string firstFrom, string firstTo, string secondFrom, string secondTo)
    {
        if (!TryGetNumericRange(firstFrom, firstTo, out var first) ||
            !TryGetNumericRange(secondFrom, secondTo, out var second))
            return string.Equals(firstFrom, secondFrom, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(firstTo, secondTo, StringComparison.OrdinalIgnoreCase);
        var firstPrefix = NumericSuffix.Match(firstFrom).Groups[1].Value;
        var secondPrefix = NumericSuffix.Match(secondFrom).Groups[1].Value;
        return string.Equals(firstPrefix, secondPrefix, StringComparison.OrdinalIgnoreCase) &&
               first.Start <= second.End && second.Start <= first.End;
    }
}
