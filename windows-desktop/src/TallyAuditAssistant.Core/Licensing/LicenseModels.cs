namespace TallyAuditAssistant.Core.Licensing;

public enum LicenseStatus
{
    NotActivated = 0,
    Trial = 1,
    Active = 2,
    Expired = 3,
    Suspended = 4,
    Invalid = 5
}

public enum LicenseType
{
    Trial = 0,
    Professional = 1,
    Enterprise = 2
}

public class LicenseFeatureEntitlements
{
    public int MaxCompanies { get; set; } = -1; // -1 = Unlimited
    public int MaxAuditPeriods { get; set; } = -1;
    public bool AllowGstAudit { get; set; } = true;
    public bool AllowTdsAudit { get; set; } = true;
    public bool AllowDuplicateEngine { get; set; } = true;
    public bool AllowReconciliation { get; set; } = true;
    public bool AllowSampling { get; set; } = true;
    public bool AllowWorkingPapers { get; set; } = true;
    public bool AllowPdfExcelExport { get; set; } = true;
    public bool AllowComparativeYoY { get; set; } = true;
    public bool AllowMultiUserRbac { get; set; } = true;
    public bool AllowAiAuditAssistant { get; set; } = true;
}

public class LicenseInfo
{
    public string LicenseKey { get; set; } = string.Empty;
    public LicenseType LicenseType { get; set; } = LicenseType.Trial;
    public LicenseStatus Status { get; set; } = LicenseStatus.NotActivated;
    public string RegisteredTo { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public DateTime IssuedDate { get; set; } = DateTime.UtcNow;
    public DateTime ExpiryDate { get; set; } = DateTime.UtcNow.AddDays(14);
    public string MachineBindingId { get; set; } = string.Empty;
    public LicenseFeatureEntitlements Entitlements { get; set; } = new();
    public bool IsOfflineValidated { get; set; } = true;
    public string SupportPlan { get; set; } = "Standard";

    public int RemainingTrialDays => Status == LicenseStatus.Trial 
        ? Math.Max(0, (int)(ExpiryDate.Date - DateTime.UtcNow.Date).TotalDays)
        : 0;

    public bool IsUsable => Status == LicenseStatus.Active || (Status == LicenseStatus.Trial && RemainingTrialDays > 0);
}

public class LicenseActivationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public LicenseInfo? License { get; set; }
}

public class UpdateInfo
{
    public string CurrentVersion { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public bool IsUpdateAvailable { get; set; }
    public string ReleaseNotes { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string Sha256Checksum { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}
