namespace TallyAuditAssistant.Engine.Gst;

/// <summary>
/// Reviews an explicitly imported IMS action snapshot against purchase-book invoices.
/// This is a workpaper only: it does not connect to GSTN, determine ITC eligibility,
/// infer statutory deemed acceptance, or submit/alter an IMS action.
/// </summary>
public sealed class GstImsActionReviewService
{
    public GstImsActionReviewReport Review(
        IReadOnlyCollection<GstImsInvoiceAction> portalRows,
        IReadOnlyCollection<GstImsBooksInvoice> booksRows,
        decimal valueTolerance = 1m)
    {
        ArgumentNullException.ThrowIfNull(portalRows);
        ArgumentNullException.ThrowIfNull(booksRows);
        if (valueTolerance < 0) throw new ArgumentOutOfRangeException(nameof(valueTolerance));

        var issues = new List<GstImsValidationIssue>();
        var valid = new List<(GstImsInvoiceAction Row, int Index, string Key)>();
        var index = 0;
        foreach (var row in portalRows)
        {
            index++;
            if (row is null) { issues.Add(new(index, "NULL_ROW", "IMS source row is null.")); continue; }
            var gstin = NormalizeGstin(row.SupplierGstin);
            var invoice = NormalizeInvoice(row.InvoiceNumber);
            if (!IsValidGstin(gstin))
            {
                issues.Add(new(index, "INVALID_SUPPLIER_GSTIN", "Supplier GSTIN format or checksum is invalid."));
                continue;
            }
            if (invoice.Length == 0)
            {
                issues.Add(new(index, "MISSING_INVOICE_NUMBER", "Invoice number is required."));
                continue;
            }
            if (row.InvoiceValue < 0)
            {
                issues.Add(new(index, "NEGATIVE_INVOICE_VALUE", "Invoice value cannot be negative."));
                continue;
            }
            if (!Enum.IsDefined(row.Action))
            {
                issues.Add(new(index, "INVALID_ACTION", "IMS action value is not recognised."));
                continue;
            }
            valid.Add((row, index, gstin + "|" + invoice));
        }

        var duplicateKeys = valid.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var duplicate in valid.Where(x => duplicateKeys.Contains(x.Key)))
            issues.Add(new(duplicate.Index, "DUPLICATE_INVOICE_REFERENCE",
                "More than one IMS row has the same normalized supplier GSTIN and invoice number. Resolve amendments/document types before matching."));

        var validBooks = new List<(GstImsBooksInvoice Row, string Key)>();
        var booksIndex = 0;
        foreach (var bookRow in booksRows)
        {
            booksIndex++;
            if (bookRow is null)
            {
                issues.Add(new(booksIndex, "BOOKS_NULL_ROW", "Purchase-book source row is null."));
                continue;
            }

            var bookGstin = NormalizeGstin(bookRow.SupplierGstin);
            var bookInvoice = NormalizeInvoice(bookRow.InvoiceNumber);
            if (!IsValidGstin(bookGstin))
            {
                issues.Add(new(booksIndex, "BOOKS_INVALID_SUPPLIER_GSTIN", "Purchase-book supplier GSTIN format or checksum is invalid."));
                continue;
            }
            if (bookInvoice.Length == 0)
            {
                issues.Add(new(booksIndex, "BOOKS_MISSING_INVOICE_NUMBER", "Purchase-book invoice number is required."));
                continue;
            }
            if (bookRow.InvoiceValue < 0)
            {
                issues.Add(new(booksIndex, "BOOKS_NEGATIVE_INVOICE_VALUE", "Purchase-book invoice value cannot be negative."));
                continue;
            }

            validBooks.Add((bookRow, bookGstin + "|" + bookInvoice));
        }

        var books = validBooks
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => (Row: x.Row, Key: x.Key)).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var consumedBooks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new List<GstImsReviewItem>();
        foreach (var entry in valid)
        {
            var row = entry.Row;
            var gstin = NormalizeGstin(row.SupplierGstin);
            var invoice = NormalizeInvoice(row.InvoiceNumber);
            if (duplicateKeys.Contains(entry.Key))
            {
                output.Add(new(GstImsReviewStatus.DuplicateReference, gstin, row.InvoiceNumber,
                    row.InvoiceDate, row.InvoiceValue, null, null, null,
                    "Duplicate supplier/invoice key; no books match is asserted until the duplicate is resolved."));
                continue;
            }

            var hasKey = books.TryGetValue(entry.Key, out var matches);
            if (hasKey && matches!.Length > 1)
            {
                output.Add(new(GstImsReviewStatus.PortalOnly, gstin, row.InvoiceNumber,
                    row.InvoiceDate, row.InvoiceValue, null, null, null,
                    "Multiple purchase-book invoices share this supplier/invoice key; no match is asserted until the books duplicates are resolved."));
                continue;
            }
            var hasBooks = hasKey && matches!.Length == 1;
            var book = hasBooks ? matches![0].Row : null;
            if (hasBooks) consumedBooks.Add(entry.Key);

            var status = row.Action switch
            {
                GstImsSupplierAction.Accepted => GstImsReviewStatus.Accepted,
                GstImsSupplierAction.Rejected => GstImsReviewStatus.Rejected,
                GstImsSupplierAction.Pending => GstImsReviewStatus.Pending,
                GstImsSupplierAction.NoAction => GstImsReviewStatus.NoAction,
                GstImsSupplierAction.DeemedAccepted => GstImsReviewStatus.DeemedAccepted,
                _ => GstImsReviewStatus.UnknownAction
            };
            var difference = book is null ? (decimal?)null : Math.Abs(row.InvoiceValue - book.InvoiceValue);
            var explanation = status switch
            {
                GstImsReviewStatus.Rejected => "Supplier invoice is marked rejected in the supplied IMS snapshot; investigate the reason and supporting evidence.",
                GstImsReviewStatus.Pending => "Supplier invoice is pending in the supplied IMS snapshot; review before the applicable action cut-off.",
                GstImsReviewStatus.NoAction => "No action is recorded in the supplied IMS snapshot; confirm whether review/action is still required.",
                GstImsReviewStatus.DeemedAccepted => "Source explicitly labels this row deemed accepted; verify the source status and applicable legal conditions independently.",
                GstImsReviewStatus.UnknownAction => "Action is not recognised; do not infer acceptance.",
                _ => "Invoice is marked accepted in the supplied IMS snapshot; acceptance alone does not establish ITC eligibility."
            };
            if (book is null)
                explanation += " No unique purchase-book invoice was found for this supplier/invoice key.";
            else if (difference > valueTolerance)
                explanation += " Invoice value differs from the purchase books; investigate value, tax and amendment details.";
            output.Add(new(status, gstin, row.InvoiceNumber, row.InvoiceDate, row.InvoiceValue,
                book?.InvoiceValue, book?.VoucherNumber, difference, explanation));
        }

        foreach (var pair in books.Where(x => x.Value.Length > 1))
        {
            foreach (var duplicate in pair.Value)
            {
                var book = duplicate.Row;
                output.Add(new(GstImsReviewStatus.DuplicateBooksReference, NormalizeGstin(book.SupplierGstin),
                    book.InvoiceNumber, book.InvoiceDate, null, book.InvoiceValue, book.VoucherNumber,
                    null, "More than one purchase-book invoice has the same normalized supplier GSTIN and invoice number; resolve the duplicate before reconciliation."));
            }
        }

        foreach (var pair in books)
        {
            if (pair.Value.Length != 1 || consumedBooks.Contains(pair.Key)) continue;
            var book = pair.Value[0].Row;
            output.Add(new(GstImsReviewStatus.BooksOnly, NormalizeGstin(book.SupplierGstin),
                book.InvoiceNumber, book.InvoiceDate, null, book.InvoiceValue, book.VoucherNumber,
                null, "Purchase-book invoice has no corresponding valid IMS row in the supplied snapshot."));
        }

        return new GstImsActionReviewReport
        {
            SourceRows = portalRows.Count,
            ValidRows = valid.Count,
            Items = output.OrderBy(x => x.Status).ThenBy(x => x.SupplierGstin)
                .ThenBy(x => x.InvoiceNumber, StringComparer.OrdinalIgnoreCase).ToArray(),
            Issues = issues
        };
    }

    private static bool IsValidGstin(string value)
    {
        if (value.Length != 15 || !value[..2].All(char.IsDigit) ||
            value[0] == '0' || value[1] == '0' ||
            !System.Text.RegularExpressions.Regex.IsMatch(value[2..12], "^[A-Z]{5}[0-9]{4}[A-Z]$") ||
            !char.IsLetterOrDigit(value[12]) || value[13] != 'Z')
            return false;

        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var sum = 0;
        for (var i = 0; i < 14; i++)
        {
            var digit = alphabet.IndexOf(value[i]);
            if (digit < 0) return false;
            var product = digit * (i % 2 == 0 ? 1 : 2);
            sum += product / 36 + product % 36;
        }

        var checkValue = (36 - sum % 36) % 36;
        return value[14] == alphabet[checkValue];
    }

    private static string NormalizeGstin(string? value) =>
        (value ?? string.Empty).Trim().Replace(" ", string.Empty).ToUpperInvariant();

    private static string NormalizeInvoice(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
