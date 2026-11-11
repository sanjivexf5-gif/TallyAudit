using System.Text.RegularExpressions;
using TallyAuditAssistant.Core.Domain.Ledgers;

namespace TallyAuditAssistant.Engine.Vendors;

public enum VendorMasterRiskLevel { High, Review, Information }

public sealed record VendorMasterFinding(
    string LedgerName,
    string ParentGroup,
    string? Gstin,
    string? RecordedPan,
    string? DerivedPan,
    VendorMasterRiskLevel RiskLevel,
    string Finding,
    string AuditorAction);

/// <summary>Offline vendor-master screening. It validates format and internal consistency only;
/// it does not establish GST registration status or legal validity with the GSTN portal.</summary>
public sealed class VendorGstinPanIntelligenceService
{
    private static readonly Regex GstinPattern = new(
        @"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PanPattern = new(
        @"^[A-Z]{5}[0-9]{4}[A-Z]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyList<VendorMasterFinding> Analyze(IReadOnlyList<Ledger> ledgers)
    {
        ArgumentNullException.ThrowIfNull(ledgers);

        var normalized = ledgers.Select(l => new
        {
            Ledger = l,
            Gstin = Normalize(l.GSTIN),
            Pan = Normalize(l.PAN)
        }).ToArray();

        var duplicateGstins = normalized
            .Where(x => x.Gstin.Length > 0 && GstinPattern.IsMatch(x.Gstin))
            .GroupBy(x => x.Gstin, StringComparer.Ordinal)
            .Where(g => g.Select(x => x.Ledger.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        var findings = new List<VendorMasterFinding>();
        foreach (var item in normalized)
        {
            if (item.Gstin.Length == 0 && item.Pan.Length == 0) continue;

            var derivedPan = item.Gstin.Length == 15 && GstinPattern.IsMatch(item.Gstin)
                ? item.Gstin.Substring(2, 10)
                : null;

            VendorMasterRiskLevel level;
            string finding;
            string action;

            if (item.Gstin.Length > 0 && !GstinPattern.IsMatch(item.Gstin))
            {
                level = VendorMasterRiskLevel.High;
                finding = "GSTIN format is invalid or incomplete.";
                action = "Verify the GST registration certificate or official GST portal record; correct the ledger only against source evidence.";
            }
            else if (derivedPan != null && item.Pan.Length > 0 && !PanPattern.IsMatch(item.Pan))
            {
                level = VendorMasterRiskLevel.High;
                finding = "Recorded PAN format is invalid.";
                action = "Verify PAN from the vendor's authoritative documents before updating the master.";
            }
            else if (derivedPan != null && item.Pan.Length > 0 &&
                     !string.Equals(derivedPan, item.Pan, StringComparison.Ordinal))
            {
                level = VendorMasterRiskLevel.High;
                finding = "PAN embedded in GSTIN does not match the PAN recorded in the ledger.";
                action = "Obtain the vendor's PAN and GST registration evidence; resolve the mismatch before relying on this master.";
            }
            else if (item.Gstin.Length > 0 && duplicateGstins.Contains(item.Gstin))
            {
                level = VendorMasterRiskLevel.Review;
                finding = "The same GSTIN is assigned to multiple ledger names in this company.";
                action = "Check whether these are duplicate vendor masters, branches, or legitimate aliases; consolidate only after review.";
            }
            else if (derivedPan != null && item.Pan.Length == 0)
            {
                level = VendorMasterRiskLevel.Review;
                finding = "PAN can be derived from the structurally valid GSTIN but is not separately recorded.";
                action = "Confirm the embedded PAN against vendor documents; populate the PAN field only after verification.";
            }
            else if (item.Gstin.Length == 0 && item.Pan.Length > 0 && !PanPattern.IsMatch(item.Pan))
            {
                level = VendorMasterRiskLevel.High;
                finding = "Recorded PAN format is invalid.";
                action = "Verify the PAN from authoritative vendor documentation and correct the master only after confirmation.";
            }
            else
            {
                level = VendorMasterRiskLevel.Information;
                finding = "GSTIN and recorded PAN are structurally consistent.";
                action = "Retain source evidence. Structural consistency does not confirm active registration or vendor identity.";
            }

            findings.Add(new VendorMasterFinding(
                item.Ledger.Name, item.Ledger.ParentGroup, item.Gstin.Length == 0 ? null : item.Gstin,
                item.Pan.Length == 0 ? null : item.Pan, derivedPan, level, finding, action));
        }

        return findings;
    }

    private static string Normalize(string? value) =>
        Regex.Replace(value ?? string.Empty, @"\s+", string.Empty).ToUpperInvariant();
}
