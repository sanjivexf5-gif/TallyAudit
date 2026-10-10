using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Core.Licensing;
using TallyAuditAssistant.Engine.Services;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class LicensingServiceTests
{
    private static (Mock<ISecureStorage> Storage, Dictionary<string, string> Values) CreateStorage()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var storage = new Mock<ISecureStorage>();
        storage.Setup(s => s.GetSecret(It.IsAny<string>()))
            .Returns((string key) => values.TryGetValue(key, out var value) ? value : null);
        storage.Setup(s => s.SetSecret(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((key, value) => values[key] = value);
        return (storage, values);
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static (string Token, string PublicKeyPem) SignLicense(LicenseInfo license)
    {
        using var rsa = RSA.Create(2048);
        var payload = JsonSerializer.SerializeToUtf8Bytes(license);
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return ($"{ToBase64Url(payload)}.{ToBase64Url(signature)}", rsa.ExportSubjectPublicKeyInfoPem());
    }

    [Fact]
    public async Task GetCurrentLicense_WhenNoTokenExists_ReturnsUnactivatedTrial()
    {
        var (storage, _) = CreateStorage();
        var service = new LicenseService(storage.Object, new Mock<IAuditTrailService>().Object,
            NullLogger<LicenseService>.Instance);

        var license = await service.GetCurrentLicenseAsync();

        Assert.NotNull(license);
        Assert.Equal(LicenseStatus.NotActivated, license.Status);
        Assert.False(service.CanUseFeature("ExportReports"));
        Assert.False(service.CanUseFeature("UnknownFeature"));
    }

    [Fact]
    public async Task ActivateLicense_WithSignedProToken_VerifiesAndPersistsAllEntitlements()
    {
        const string key = "TAA-2026-PRO-1234-5678";
        var license = new LicenseInfo
        {
            LicenseKey = key,
            LicenseType = LicenseType.Professional,
            Status = LicenseStatus.Active,
            RegisteredTo = "Auditor Name",
            Organization = "Example Audit Practice",
            IssuedDate = DateTime.UtcNow.AddMinutes(-1),
            ExpiryDate = DateTime.UtcNow.AddDays(365),
            MachineBindingId = "SIGNED-DEVICE-CLAIM",
            Entitlements = new LicenseFeatureEntitlements
            {
                MaxCompanies = 3,
                MaxAuditPeriods = 2,
                AllowGstAudit = true,
                AllowTdsAudit = true,
                AllowDuplicateEngine = true,
                AllowReconciliation = true,
                AllowSampling = false,
                AllowWorkingPapers = true,
                AllowPdfExcelExport = false,
                AllowComparativeYoY = true,
                AllowMultiUserRbac = false,
                AllowAiAuditAssistant = false
            }
        };
        var (token, publicKey) = SignLicense(license);
        var (storage, values) = CreateStorage();
        var service = new LicenseService(storage.Object, new Mock<IAuditTrailService>().Object,
            NullLogger<LicenseService>.Instance, publicKey);

        var result = await service.ActivateLicenseAsync(key, token);

        Assert.True(result.Success);
        Assert.NotNull(result.License);
        Assert.Equal(LicenseStatus.Active, result.License.Status);
        Assert.Equal(LicenseType.Professional, result.License.LicenseType);
        Assert.False(result.License.Entitlements.AllowPdfExcelExport);
        Assert.False(result.License.Entitlements.AllowSampling);
        Assert.StartsWith("{", values["AppLicense_MasterToken"]);

        var reloadedService = new LicenseService(storage.Object, new Mock<IAuditTrailService>().Object,
            NullLogger<LicenseService>.Instance, publicKey);
        var reloaded = await reloadedService.GetCurrentLicenseAsync();
        Assert.Equal(LicenseStatus.Active, reloaded.Status);
        Assert.Equal(3, reloaded.Entitlements.MaxCompanies);
        Assert.False(reloaded.Entitlements.AllowAiAuditAssistant);
        Assert.False(reloadedService.CanUseFeature("FeatureNotInRegistry"));
    }

    [Fact]
    public async Task ActivateLicense_RejectsBareKeyAndForgedToken()
    {
        const string key = "TAA-2026-PRO-UNSIGNED";
        var (storage, values) = CreateStorage();
        using var rsa = RSA.Create(2048);
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var service = new LicenseService(storage.Object, new Mock<IAuditTrailService>().Object,
            NullLogger<LicenseService>.Instance, publicKey);

        var bareKeyResult = await service.ActivateLicenseAsync(key);
        Assert.False(bareKeyResult.Success);

        var unsignedPayload = JsonSerializer.SerializeToUtf8Bytes(new LicenseInfo
        {
            LicenseKey = key,
            LicenseType = LicenseType.Professional,
            Status = LicenseStatus.Active,
            RegisteredTo = "Forged",
            Organization = "Forged",
            IssuedDate = DateTime.UtcNow.AddMinutes(-1),
            ExpiryDate = DateTime.UtcNow.AddDays(365),
            MachineBindingId = "FAKE"
        });
        var forged = $"{ToBase64Url(unsignedPayload)}.{ToBase64Url(new byte[256])}";
        var forgedResult = await service.ActivateLicenseAsync(key, forged);

        Assert.False(forgedResult.Success);
        Assert.Empty(values);
    }

    [Fact]
    public async Task StartTrial_CreatesFourteenDayTrial_AndCannotBeRepeated()
    {
        var (storage, _) = CreateStorage();
        var auditTrail = new Mock<IAuditTrailService>().Object;
        var service = new LicenseService(storage.Object, auditTrail, NullLogger<LicenseService>.Instance);

        var result = await service.StartTrialAsync("Acme CA Audit", "auditor@acme.com");

        Assert.True(result.Success);
        Assert.NotNull(result.License);
        Assert.Equal(LicenseStatus.Trial, result.License.Status);
        Assert.InRange(result.License.RemainingTrialDays, 13, 14);

        var secondAttempt = await service.StartTrialAsync("Acme CA Audit", "auditor@acme.com");
        Assert.False(secondAttempt.Success);
        Assert.Contains("already been started", secondAttempt.Message, StringComparison.OrdinalIgnoreCase);
    }
}
