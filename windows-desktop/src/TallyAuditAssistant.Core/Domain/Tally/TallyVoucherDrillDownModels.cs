using System.Text.Json.Serialization;

namespace TallyAuditAssistant.Core.Domain.Tally;

public class TallyGstDetailSnapshot
{
    public string? PartyGstin { get; set; }
    public string? RegistrationType { get; set; } // Regular, Composition, Unregistered, Overseas
    public string? PlaceOfSupply { get; set; }
    public string? PlaceOfSupplyStateCode { get; set; }
    public bool IsReverseCharge { get; set; }
    public decimal TaxableValue { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal CessAmount { get; set; }
    public decimal TotalTaxAmount => CgstAmount + SgstAmount + IgstAmount + CessAmount;
    public string? HsnOrSacCodes { get; set; }
    public decimal? ApplicableGstRate { get; set; }
}

public class TallyTdsDetailSnapshot
{
    public string? Section { get; set; } // 194C, 194J, 194I, 194H, 206AA
    public string? PayeePan { get; set; }
    public bool IsPanValid { get; set; }
    public string? DeducteeType { get; set; } // Company, Individual/HUF, Non-Resident
    public decimal SingleBillThreshold { get; set; }
    public decimal CumulativeThreshold { get; set; }
    public decimal TdsRate { get; set; }
    public decimal DeductibleBaseAmount { get; set; }
    public decimal TdsWithheldAmount { get; set; }
    public string? LowerDeductionCertificateNo { get; set; }
    public string? Form15G15HStatus { get; set; }
}

public class TallyVoucherLinePosting
{
    public string EntryId { get; set; } = string.Empty;
    public string LedgerName { get; set; } = string.Empty;
    public string ParentGroup { get; set; } = string.Empty;
    public string PrimaryClassification { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsDebit { get; set; }
    public string? HsnOrSac { get; set; }
    public decimal? TaxOrTdsRate { get; set; }
}

public class TallyNavigationBreadcrumb
{
    public string GatewayPath { get; set; } = string.Empty;
    public string KeyboardShortcuts { get; set; } = string.Empty;
    public string GoToSearchQuery { get; set; } = string.Empty;
    public string DayBookFilterDate { get; set; } = string.Empty;
    public string TdlXmlPayload { get; set; } = string.Empty;
}

public class TallyOpenAttemptResult
{
    public bool IsDirectLaunchSuccess { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public string MethodAttempted { get; set; } = string.Empty;
    public bool RequiresManualNavigationFallback { get; set; }
    public TallyNavigationBreadcrumb NavigationGuide { get; set; } = new();
    public DateTime AttemptTimestamp { get; set; } = DateTime.UtcNow;
}

public class TallyVoucherDrillDownInfo
{
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyGuid { get; set; } = string.Empty;
    public string VoucherId { get; set; } = string.Empty;
    public string MasterId { get; set; } = string.Empty;
    public string AlterId { get; set; } = string.Empty;
    public string VoucherNumber { get; set; } = string.Empty;
    public string VoucherTypeName { get; set; } = string.Empty;
    public DateTime VoucherDate { get; set; }
    public string FormattedVoucherDate => VoucherDate.ToString("dd-MMM-yyyy");
    public string? ReferenceNumber { get; set; }
    public string PartyLedgerName { get; set; } = string.Empty;
    public string PrimaryLedgerHead { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string? Narration { get; set; }
    
    public TallyGstDetailSnapshot? GstDetails { get; set; }
    public TallyTdsDetailSnapshot? TdsDetails { get; set; }
    public List<TallyVoucherLinePosting> Entries { get; set; } = new();
    
    public TallyNavigationBreadcrumb NavigationGuide { get; set; } = new();
}
