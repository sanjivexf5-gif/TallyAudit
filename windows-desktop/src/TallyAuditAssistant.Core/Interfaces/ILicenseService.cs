using TallyAuditAssistant.Core.Licensing;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ILicenseService
{
    Task<LicenseInfo> GetCurrentLicenseAsync(CancellationToken ct = default);
    Task<LicenseActivationResult> ActivateLicenseAsync(string licenseKey, string? offlineActivationToken = null, CancellationToken ct = default);
    Task<LicenseActivationResult> StartTrialAsync(string organizationName, string contactEmail, CancellationToken ct = default);
    bool CanUseFeature(string featureName);
    bool CanAddCompany(int currentCompanyCount);
    bool CanAddPeriod(int currentPeriodCount);
    Task<bool> ValidateLicenseIntegrityAsync(CancellationToken ct = default);
}
