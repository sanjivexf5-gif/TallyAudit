using TallyAuditAssistant.Core.Domain.Ledgers;
using TallyAuditAssistant.Engine.Vendors;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class VendorGstinPanIntelligenceServiceTests
{
    private readonly VendorGstinPanIntelligenceService _service = new();

    [Fact]
    public void Analyze_DetectsPanMismatch()
    {
        var result = Assert.Single(_service.Analyze(new[]
        {
            new Ledger { Name = "Vendor A", ParentGroup = "Sundry Creditors",
                GSTIN = "27AAACB2222D1Z9", PAN = "AAACB2222E" }
        }));

        Assert.Equal(VendorMasterRiskLevel.High, result.RiskLevel);
        Assert.Contains("does not match", result.Finding);
        Assert.Equal("AAACB2222D", result.DerivedPan);
    }

    [Fact]
    public void Analyze_FlagsMalformedGstin()
    {
        var result = Assert.Single(_service.Analyze(new[]
        {
            new Ledger { Name = "Vendor A", ParentGroup = "Sundry Creditors", GSTIN = "27INVALID" }
        }));
        Assert.Equal(VendorMasterRiskLevel.High, result.RiskLevel);
    }

    [Fact]
    public void Analyze_FlagsDuplicateGstinAcrossLedgers()
    {
        var result = _service.Analyze(new[]
        {
            new Ledger { Name = "Vendor A", GSTIN = "27AAACB2222D1Z9" },
            new Ledger { Name = "Vendor A Alias", GSTIN = "27AAACB2222D1Z9" }
        });
        Assert.All(result, x => Assert.Equal(VendorMasterRiskLevel.Review, x.RiskLevel));
    }

    [Fact]
    public void Analyze_DoesNotFlagMultipleGstinsForSamePan()
    {
        var result = _service.Analyze(new[]
        {
            new Ledger { Name = "Vendor West", GSTIN = "27AAACB2222D1Z9", PAN = "AAACB2222D" },
            new Ledger { Name = "Vendor Karnataka", GSTIN = "29AAACB2222D1Z7", PAN = "AAACB2222D" }
        });
        Assert.All(result, x => Assert.Equal(VendorMasterRiskLevel.Information, x.RiskLevel));
    }

    [Fact]
    public void Analyze_IgnoresLedgersWithoutTaxIdentifiers()
    {
        Assert.Empty(_service.Analyze(new[] { new Ledger { Name = "Cash", ParentGroup = "Cash-in-Hand" } }));
    }

    [Fact]
    public void Analyze_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Analyze(null!));
    }
}
