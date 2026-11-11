using TallyAuditAssistant.Engine.Gst;

namespace TallyAuditAssistant.Engine.Gst;

/// <summary>
/// Converts GSTR-2B reconciliation outcomes into audit follow-up priorities.
/// It deliberately does not calculate admissible ITC or make legal eligibility decisions.
/// </summary>
public sealed class ItcRiskScreeningService
{
    public IReadOnlyList<ItcRiskScreeningItem> Assess(Gstr2BReconciliationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return report.Items.Select(item =>
        {
            var (risk, finding, action) = item.Status switch
            {
                Gstr2BMatchStatus.BooksOnly => (ItcRiskLevel.High,
                    "Purchase exists in Tally but was not found in the imported GSTR-2B data.",
                    "Check supplier filing, amendments, document type, GSTIN, tax period and subsequent GSTR-2B before considering credit."),
                Gstr2BMatchStatus.GstinMismatch => (ItcRiskLevel.High,
                    "Supplier GSTIN differs between Tally and GSTR-2B.",
                    "Verify the original tax invoice and supplier identity; correct master data only after documentary confirmation."),
                Gstr2BMatchStatus.ValueMismatch => (ItcRiskLevel.High,
                    "Invoice value differs between Tally and GSTR-2B.",
                    "Reconcile taxable value, tax heads, credit/debit notes, amendments and rounding against the source invoice."),
                Gstr2BMatchStatus.Gstr2BOnly => (ItcRiskLevel.Information,
                    "GSTR-2B invoice has no matching purchase voucher in the selected Tally company and period.",
                    "Check whether the invoice is unbooked, booked in another period/company, or requires accounting review; do not claim from this result alone."),
                _ => (ItcRiskLevel.Review,
                    "Invoice identity, supplier GSTIN and invoice value match within tolerance.",
                    "Matching is only one screening step. Verify invoice/debit note, receipt of goods or services, business use, section 17 restrictions, payment within 180 days, supplier tax compliance and time limit.")
            };

            return new ItcRiskScreeningItem(item.InvoiceNumber, item.TallyVoucherNumber,
                item.SupplierGstin, risk, finding, action,
                "CGST Act sections 16 and 17; CGST Rules 36 and 37. A GSTR-2B match alone does not establish ITC eligibility.");
        }).ToArray();
    }
}

public enum ItcRiskLevel { High, Review, Information }

public sealed record ItcRiskScreeningItem(
    string InvoiceNumber,
    string? TallyVoucherNumber,
    string? SupplierGstin,
    ItcRiskLevel RiskLevel,
    string Finding,
    string AuditorAction,
    string LegalReference);
