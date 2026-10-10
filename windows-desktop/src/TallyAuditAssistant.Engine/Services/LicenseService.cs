using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private const string TrialStartedStorageKey = "AppLicense_TrialStartedAt";
    private readonly string? _licensePublicKeyPem;

    public LicenseService(
        ISecureStorage secureStorage,
        IAuditTrailService auditTrailService,
        ILogger<LicenseService> logger,
        string? licensePublicKeyPem = null)
    {
        _secureStorage = secureStorage;
        _auditTrailService = auditTrailService;
        _logger = logger;
        _licensePublicKeyPem = string.IsNullOrWhiteSpace(licensePublicKeyPem)
            ? Environment.GetEnvironmentVariable("TALLY_AUDIT_LICENSE_PUBLIC_KEY")
            : licensePublicKeyPem;
    }

    public async Task<LicenseInfo> GetCurrentLicenseAsync(CancellationToken ct = default)
    {
        if (_cachedLicense != null)
            return _cachedLicense;

        var stored = _secureStorage.GetSecret(LicenseStorageKey);
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
        var normalizedKey = (licenseKey ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedKey.Length == 0)
        {
            return new LicenseActivationResult { Success = false, Message = "License key cannot be empty." };
        }

        if (string.IsNullOrWhiteSpace(offlineActivationToken))
        {
            return new LicenseActivationResult
            {
                Success = false,
                Message = "A signed offline activation token is required. A license key alone cannot be validated."
            };
        }

        if (string.IsNullOrWhiteSpace(_licensePublicKeyPem))
        {
            return new LicenseActivationResult
            {
                Success = false,
                Message = "Secure license verification is not configured on this installation. Set TALLY_AUDIT_LICENSE_PUBLIC_KEY to the issuer's RSA public key; the supplied key has not been activated."
            };
        }

        try
        {
            var separator = offlineActivationToken.IndexOf('.');
            if (separator <= 0 || separator != offlineActivationToken.LastIndexOf('.') ||
                separator == offlineActivationToken.Length - 1)
            {
                return InvalidActivationResult("The signed activation token has an invalid format.");
            }

            var payloadBytes = DecodeBase64Url(offlineActivationToken[..separator]);
            var signatureBytes = DecodeBase64Url(offlineActivationToken[(separator + 1)..]);

            using var rsa = RSA.Create();
            rsa.ImportFromPem(_licensePublicKeyPem);
            if (!rsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                return InvalidActivationResult("The activation token signature is invalid.");
            }

            var license = JsonSerializer.Deserialize<LicenseInfo>(payloadBytes, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (license == null)
            {
                return InvalidActivationResult("The activation token payload is empty or invalid.");
            }

            var signedLicenseKey = (license.LicenseKey ?? string.Empty).Trim().ToUpperInvariant();
            if (!string.Equals(signedLicenseKey, normalizedKey, StringComparison.Ordinal))
            {
                return InvalidActivationResult("The activation token does not match the supplied license key.");
            }

            var now = DateTime.UtcNow;
            if (license.LicenseType is not (LicenseType.Professional or LicenseType.Enterprise) ||
                license.Status != LicenseStatus.Active ||
                license.IssuedDate > now.AddMinutes(5) ||
                license.ExpiryDate <= now ||
                license.ExpiryDate <= license.IssuedDate ||
                string.IsNullOrWhiteSpace(license.RegisteredTo) ||
                string.IsNullOrWhiteSpace(license.Organization) ||
                string.IsNullOrWhiteSpace(license.MachineBindingId) ||
                license.Entitlements == null)
            {
                return InvalidActivationResult("The signed license claims are incomplete, not yet valid, expired, or unsupported.");
            }

            license.LicenseKey = normalizedKey;
            license.IsOfflineValidated = true;
            _cachedLicense = license;
            _secureStorage.SetSecret(LicenseStorageKey, SerializeLicenseToken(license));

            await _auditTrailService.RecordActivityAsync(
                actionType: "LICENSE_ACTIVATION",
                module: "SYSTEM",
                description: $"Successfully activated a signature-verified {license.LicenseType} license.",
                ct: ct);

            return new LicenseActivationResult
            {
                Success = true,
                Message = $"License successfully verified and activated. Product Edition: {license.LicenseType}.",
                License = license
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Secure offline license activation was rejected.");
            return InvalidActivationResult("The activation token could not be verified. Confirm the token and issuer public key.");
        }
    }

    private static LicenseActivationResult InvalidActivationResult(string message) =>
        new() { Success = false, Message = message };

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }

    public async Task<LicenseActivationResult> StartTrialAsync(string organizationName, string contactEmail, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_secureStorage.GetSecret(TrialStartedStorageKey)))
        {
            return new LicenseActivationResult
            {
                Success = false,
                Message = "A trial has already been started on this installation. Please activate a valid signed license to continue."
            };
        }

        var existingLicense = await GetCurrentLicenseAsync(ct);
        if (existingLicense.Status == LicenseStatus.Active)
        {
            return new LicenseActivationResult
            {
                Success = false,
                Message = "An active license is already installed; a separate trial cannot be started."
            };
        }

        if (string.IsNullOrWhiteSpace(organizationName) || string.IsNullOrWhiteSpace(contactEmail))
        {
            return new LicenseActivationResult
            {
                Success = false,
                Message = "Organization name and contact email are required to start a trial."
            };
        }

        _logger.LogInformation("Starting 14-day evaluation trial for {Org}", organizationName);

        var trialLicense = new LicenseInfo
        {
            LicenseKey = "TAA-TRIAL-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            LicenseType = LicenseType.Trial,
            Status = LicenseStatus.Trial,
            RegisteredTo = contactEmail.Trim(),
            Organization = organizationName.Trim(),
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
        _secureStorage.SetSecret(TrialStartedStorageKey, trialLicense.IssuedDate.ToString("O"));
        _secureStorage.SetSecret(LicenseStorageKey, SerializeLicenseToken(trialLicense));

        await _auditTrailService.RecordActivityAsync(
            actionType: "TRIAL_ACTIVATION",
            module: "SYSTEM",
            description: $"14-day evaluation trial started for {trialLicense.Organization}.",
            ct: ct);

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
            _ => false
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

    private static string SerializeLicenseToken(LicenseInfo info) =>
        JsonSerializer.Serialize(info);

    private static LicenseInfo ParseLicenseToken(string token)
    {
        // Legacy pipe-delimited tokens were unsigned and could be created from any
        // non-empty key. Do not continue trusting those unverifiable tokens.
        if (string.IsNullOrWhiteSpace(token) || !token.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            return new LicenseInfo
            {
                Status = LicenseStatus.Invalid,
                IsOfflineValidated = false,
                LicenseKey = string.Empty
            };
        }

        try
        {
            var license = JsonSerializer.Deserialize<LicenseInfo>(token, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (license == null || string.IsNullOrWhiteSpace(license.LicenseKey))
            {
                return new LicenseInfo { Status = LicenseStatus.Invalid, IsOfflineValidated = false };
            }

            if (license.ExpiryDate <= DateTime.UtcNow)
                license.Status = LicenseStatus.Expired;

            return license;
        }
        catch (JsonException)
        {
            return new LicenseInfo { Status = LicenseStatus.Invalid, IsOfflineValidated = false };
        }
    }
}
