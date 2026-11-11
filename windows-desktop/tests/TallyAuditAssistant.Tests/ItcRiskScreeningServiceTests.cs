using TallyAuditAssistant.Engine.Gst;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class ItcRiskScreeningServiceTests
{
    [Fact]
    public void Assess_MapsReconciliationExceptionsToAuditorActions_WithoutDeclaringEligibility()
    {
        var report = new Gstr2BReconciliationReport
        {
            CompanyId = "C1",
            FromDate = new DateTime(2025, 4, 1),
            ToDate = new DateTime(2026, 3, 31),
            Items = new[]
            {
                new Gstr2BReconciliationItem(Gstr2BMatchStatus.Matched, "INV-1", "27AAACB2222D1Z9", "P-1",
                    new DateTime(2025, 5, 1), 118000m, 118000m, 0m, "Matched"),
                new Gstr2BReconciliationItem(Gstr2BMatchStatus.BooksOnly, "INV-2", "27AAACB2222D1Z9", "P-2",
                    new DateTime(2025, 5, 2), 59000m, null, 59000m, "Not in portal extract"),
                new Gstr2BReconciliationItem(Gstr2BMatchStatus.GstinMismatch, "INV-3", "29BBBCD3333E1Z4", "P-3",
                    new DateTime(2025, 5, 3), 23600m, 23600m, 0m, "GSTIN mismatch"),
                new Gstr2BReconciliationItem(Gstr2BMatchStatus.Gstr2BOnly, "INV-4", "27AAACB2222D1Z9", null,
                    new DateTime(2025, 5, 4), null, 11800m, 11800m, "Not in books")
            }
        };

        var results = new ItcRiskScreeningService().Assess(report);

        Assert.Equal(4, results.Count);
        Assert.Equal(ItcRiskLevel.Review, results.Single(x => x.InvoiceNumber == "INV-1").RiskLevel);
        Assert.Equal(ItcRiskLevel.High, results.Single(x => x.InvoiceNumber == "INV-2").RiskLevel);
        Assert.Equal(ItcRiskLevel.High, results.Single(x => x.InvoiceNumber == "INV-3").RiskLevel);
        Assert.Equal(ItcRiskLevel.Information, results.Single(x => x.InvoiceNumber == "INV-4").RiskLevel);
        Assert.All(results, x => Assert.Contains("does not establish ITC eligibility", x.LegalReference));
        Assert.Contains("180 days", results.Single(x => x.InvoiceNumber == "INV-1").AuditorAction);
    }

    [Fact]
    public void Assess_RejectsNullReport()
    {
        Assert.Throws<ArgumentNullException>(() => new ItcRiskScreeningService().Assess(null!));
    }
}
