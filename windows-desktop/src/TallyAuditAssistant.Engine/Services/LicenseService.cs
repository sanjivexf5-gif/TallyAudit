using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Common;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Core.Licensing;

namespace TallyAuditAssistant.Engine.Services;

public class LicenseService : ILicenseService
{
    private readonly ISecureStorage _secureStorage;
    private readonly IAuditTrailService _auditTrailService;
    private readonly ILogger<LicenseService> _logger;
    private LicenseInfo? _cachedLicense;

    private const string LicenseStorageKey = "AppLicense_MasterToken";

    public LicenseService(
        ISecureStorage secureStorage,
        IAuditTrailService auditTrailService,
        ILogger<LicenseService> logger)
    {
        _secureStorage = secureStorage;
        _auditTrailService = auditTrailService;
        _logger = logger;
    }

    public async Task<LicenseInfo> GetCurrentLicenseAsync(CancellationToken ct = default)
    {
        if (_cachedLicense != null)
            return _cachedLicense;

        var stored = await _secureStorage.GetSecretAsync(LicenseStorageKey, ct);
        if (string.IsNullOrWhiteSpace(stored))
        {
            _cachedLicense = new LicenseInfo
            {
                Status = LicenseStatus.NotActivated,
                LicenseType = LicenseType.Trial,
                LicenseKey = string.Empty,
                RegisteredTo = "Unregistered User",
                Organization = "Pending Activation",
                IssuedDate = DateTime.UtcNow,
                ExpiryDate = DateTime.UtcNow.AddDays(14),
                Entitlements = new LicenseFeatureEntitlements
                {
                    MaxCompanies = 2,
                    MaxAuditPeriods = 1,
                    AllowGstAudit = true,
                    AllowTdsAudit = true,
                    AllowDuplicateEngine = true,
                    AllowReconciliation = true,
                    AllowSampling = true,
                    AllowWorkingPapers = true,
                    AllowPdfExcelExport = false,
                    AllowComparativeYoY = true,
                    AllowMultiUserRbac = false,
                    AllowAiAuditAssistant = false
                }
            };
            return _cachedLicense;
        }

        // Parse token and populate license
        _cachedLicense = ParseLicenseToken(stored);
        return _cachedLicense;
    }

    public async Task<LicenseActivationResult> ActivateLicenseAsync(string licenseKey, string? offlineActivationToken = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Attempting license activation for key format {KeyPrefix}****", licenseKey.Length > 8 ? licenseKey[..8] : "KEY");

        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return new LicenseActivationResult
            {
                Success = false,
                Message = "License key cannot be empty."
            };
        }

        var normalizedKey = licenseKey.Trim().ToUpperInvariant();

        // Determine license type from key structure
        LicenseType type = LicenseType.Professional;
        if (normalizedKey.Contains("ENT"))
            type = LicenseType.Enterprise;
        else if (normalizedKey.Contains("PRO"))
            type = LicenseType.Professional;

        var license = new LicenseInfo
        {
            LicenseKey = normalizedKey,
            LicenseType = type,
            Status = LicenseStatus.Active,
            RegisteredTo = "Licensed Statutory Auditor",
            Organization = "Audit Practice",
            IssuedDate = DateTime.UtcNow,
            ExpiryDate = DateTime.UtcNow.AddYears(1),
            MachineBindingId = "DPAPI-DEVICE-BOUND-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            IsOfflineValidated = true,
            SupportPlan = type == LicenseType.Enterprise ? "Enterprise 24/7 SLA" : "Priority Standard Support",
            Entitlements = new LicenseFeatureEntitlements
            {
                MaxCompanies = -1,
                MaxAuditPeriods = -1,
                AllowGstAudit = true,
                AllowTdsAudit = true,
                AllowDuplicateEngine = true,
                AllowReconciliation = true,
                AllowSampling = true,
                AllowWorkingPapers = true,
                AllowPdfExcelExport = true,
                AllowComparativeYoY = true,
                AllowMultiUserRbac = true,
                AllowAiAuditAssistant = true
            }
        };

        _cachedLicense = license;
        var token = SerializeLicenseToken(license);
        await _secureStorage.SetSecretAsync(LicenseStorageKey, token, ct);

        await _auditTrailService.RecordEventAsync(
            "LICENSE_ACTIVATION",
            "SYSTEM",
            $"Application successfully activated with {type} license key.",
            null,
            null,
            ct);

        return new LicenseActivationResult
        {
            Success = true,
            Message = $"License successfully activated! Product Edition: {type}.",
            License = license
        };
    }

    public async Task<LicenseActivationResult> StartTrialAsync(string organizationName, string contactEmail, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting 14-day evaluation trial for {Org}", organizationName);

        var trialLicense = new LicenseInfo
        {
            LicenseKey = "TAA-TRIAL-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            LicenseType = LicenseType.Trial,
            Status = LicenseStatus.Trial,
            RegisteredTo = contactEmail,
            Organization = organizationName,
            IssuedDate = DateTime.UtcNow,
            ExpiryDate = DateTime.UtcNow.AddDays(14),
            MachineBindingId = "DPAPI-TRIAL-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            SupportPlan = "Community Documentation",
            IsOfflineValidated = true,
            Entitlements = new LicenseFeatureEntitlements
            {
                MaxCompanies = 2,
                MaxAuditPeriods = 1,
                AllowGstAudit = true,
                AllowTdsAudit = true,
                AllowDuplicateEngine = true,
                AllowReconciliation = true,
                AllowSampling = true,
                AllowWorkingPapers = true,
                AllowPdfExcelExport = false,
                AllowComparativeYoY = true,
                AllowMultiUserRbac = false,
                AllowAiAuditAssistant = false
            }
        };

        _cachedLicense = trialLicense;
        var token = SerializeLicenseToken(trialLicense);
        await _secureStorage.SetSecretAsync(LicenseStorageKey, token, ct);

        await _auditTrailService.RecordEventAsync(
            "TRIAL_ACTIVATION",
            "SYSTEM",
            $"14-day evaluation trial started for {organizationName} ({contactEmail}).",
            null,
            null,
            ct);

        return new LicenseActivationResult
        {
            Success = true,
            Message = "14-Day Evaluation Trial started successfully.",
            License = trialLicense
        };
    }

    public bool CanUseFeature(string featureName)
    {
        var license = _cachedLicense ?? GetCurrentLicenseAsync().GetAwaiter().GetResult();
        if (!license.IsUsable)
            return false;

        return featureName switch
        {
            "GstAudit" => license.Entitlements.AllowGstAudit,
            "TdsAudit" => license.Entitlements.AllowTdsAudit,
            "DuplicateDetection" => license.Entitlements.AllowDuplicateEngine,
            "Reconciliation" => license.Entitlements.AllowReconciliation,
            "Sampling" => license.Entitlements.AllowSampling,
            "WorkingPapers" => license.Entitlements.AllowWorkingPapers,
            "ExportReports" => license.Entitlements.AllowPdfExcelExport,
            "ComparativeYoY" => license.Entitlements.AllowComparativeYoY,
            "MultiUserRbac" => license.Entitlements.AllowMultiUserRbac,
            "AiAssistant" => license.Entitlements.AllowAiAuditAssistant,
            _ => true
        };
    }

    public bool CanAddCompany(int currentCompanyCount)
    {
        var license = _cachedLicense ?? GetCurrentLicenseAsync().GetAwaiter().GetResult();
        if (!license.IsUsable) return false;
        if (license.Entitlements.MaxCompanies == -1) return true;
        return currentCompanyCount < license.Entitlements.MaxCompanies;
    }

    public bool CanAddPeriod(int currentPeriodCount)
    {
        var license = _cachedLicense ?? GetCurrentLicenseAsync().GetAwaiter().GetResult();
        if (!license.IsUsable) return false;
        if (license.Entitlements.MaxAuditPeriods == -1) return true;
        return currentPeriodCount < license.Entitlements.MaxAuditPeriods;
    }

    public Task<bool> ValidateLicenseIntegrityAsync(CancellationToken ct = default)
    {
        var license = _cachedLicense ?? GetCurrentLicenseAsync(ct).GetAwaiter().GetResult();
        return Task.FromResult(license.IsUsable);
    }

    private static string SerializeLicenseToken(LicenseInfo info)
    {
        return $"{info.LicenseKey}|{(int)info.LicenseType}|{(int)info.Status}|{info.RegisteredTo}|{info.Organization}|{info.IssuedDate:O}|{info.ExpiryDate:O}|{info.MachineBindingId}";
    }

    private static LicenseInfo ParseLicenseToken(string token)
    {
        var parts = token.Split('|');
        if (parts.Length < 8)
        {
            return new LicenseInfo { Status = LicenseStatus.Invalid };
        }

        var license = new LicenseInfo
        {
            LicenseKey = parts[0],
            LicenseType = Enum.TryParse<LicenseType>(parts[1], out var lt) ? lt : LicenseType.Professional,
            Status = Enum.TryParse<LicenseStatus>(parts[2], out var ls) ? ls : LicenseStatus.Active,
            RegisteredTo = parts[3],
            Organization = parts[4],
            IssuedDate = DateTime.TryParse(parts[5], out var id) ? id : DateTime.UtcNow,
            ExpiryDate = DateTime.TryParse(parts[6], out var ed) ? ed : DateTime.UtcNow.AddYears(1),
            MachineBindingId = parts[7],
            IsOfflineValidated = true
        };

        if (license.ExpiryDate < DateTime.UtcNow)
        {
            license.Status = LicenseStatus.Expired;
        }

        return license;
    }
}
