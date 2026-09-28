using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Core.Services;

public class ActiveCompanyContext : IActiveCompanyContext
{
    private readonly ISettingsService _settingsService;
    private readonly ITallyCompanyService _companyService;
    private readonly IAuditRepository _auditRepository;
    private readonly ILogger<ActiveCompanyContext> _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);

    public string? ActiveCompanyId { get; private set; }
    public string? ActiveCompanyName { get; private set; }

    public event EventHandler<string>? ActiveCompanyChanged;

    public ActiveCompanyContext(
        ISettingsService settingsService,
        ITallyCompanyService companyService,
        IAuditRepository auditRepository,
        ILogger<ActiveCompanyContext> logger)
    {
        _settingsService = settingsService;
        _companyService = companyService;
        _auditRepository = auditRepository;
        _logger = logger;
    }

    public async Task<string> GetActiveCompanyNameAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(ActiveCompanyName) && ActiveCompanyName != "No Company Selected")
        {
            return ActiveCompanyName;
        }

        return await InitializeCompanyContextAsync(cancellationToken);
    }

    public async Task<Company?> GetActiveCompanyRecordAsync(CancellationToken cancellationToken = default)
    {
        var name = await GetActiveCompanyNameAsync(cancellationToken);
        if (string.IsNullOrEmpty(name) || name == "No Company Selected") return null;

        var comp = await _auditRepository.GetCompanyByTallyNameAsync(name, cancellationToken);
        if (comp == null && !string.IsNullOrEmpty(ActiveCompanyId))
        {
            comp = await _auditRepository.GetCompanyByIdAsync(ActiveCompanyId, cancellationToken);
        }

        return comp;
    }

    public async Task SetActiveCompanyAsync(string companyName, string? companyId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyName)) return;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            ActiveCompanyName = companyName;
            if (!string.IsNullOrEmpty(companyId))
            {
                ActiveCompanyId = companyId;
            }
            else
            {
                var existing = await _auditRepository.GetCompanyByTallyNameAsync(companyName, cancellationToken);
                if (existing != null)
                {
                    ActiveCompanyId = existing.Id;
                }
            }

            await _settingsService.SetSettingAsync("ActiveCompany", companyName, cancellationToken);
            _logger.LogInformation("Active company context updated to: {Company} (Id: {Id})", companyName, ActiveCompanyId);
        }
        finally
        {
            _lock.Release();
        }

        ActiveCompanyChanged?.Invoke(this, companyName);
    }

    public async Task<string> InitializeCompanyContextAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        string resolvedName = string.Empty;
        try
        {
            var isMock = await _settingsService.IsMockModeEnabledAsync();

            string? targetCompany = null;
            if (isMock)
            {
                // In mock mode, the active mock company from ITallyCompanyService is authoritative:
                // "Demo Industrial Solutions Pvt Ltd (FY 2025-26)"
                targetCompany = await _companyService.GetActiveCompanyAsync(cancellationToken: cancellationToken);
            }
            else
            {
                // In real mode, check persisted settings first, or probe Tally
                var stored = await _settingsService.GetSettingAsync("ActiveCompany", string.Empty, cancellationToken);
                if (!string.IsNullOrEmpty(stored))
                {
                    targetCompany = stored;
                }
                else
                {
                    targetCompany = await _companyService.GetActiveCompanyAsync(cancellationToken: cancellationToken);
                }
            }

            if (string.IsNullOrEmpty(targetCompany))
            {
                targetCompany = await _settingsService.GetSettingAsync("ActiveCompany", string.Empty, cancellationToken);
            }

            if (!string.IsNullOrEmpty(targetCompany))
            {
                resolvedName = targetCompany;
                ActiveCompanyName = targetCompany;

                // Ensure company exists in repository
                var existing = await _auditRepository.GetCompanyByTallyNameAsync(targetCompany, cancellationToken);
                if (existing == null)
                {
                    var profile = await _companyService.GetCompanyProfileTypedAsync(targetCompany, null, cancellationToken);
                    var comp = new Company
                    {
                        Id = Guid.NewGuid().ToString(),
                        TallyCompanyName = targetCompany,
                        FormalName = profile?.FormalName ?? targetCompany,
                        GSTIN = profile?.GSTIN,
                        PAN = profile?.PAN,
                        StateName = profile?.StateName,
                        StateCode = profile?.StateCode,
                        BooksFromDate = profile?.BooksBeginningFrom ?? new DateTime(2025, 4, 1),
                        LastAlterId = profile?.AlterId ?? 0,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    var saved = await _auditRepository.EnsureCompanyAsync(comp, cancellationToken);
                    ActiveCompanyId = saved.Id;
                }
                else
                {
                    ActiveCompanyId = existing.Id;
                }

                await _settingsService.SetSettingAsync("ActiveCompany", targetCompany, cancellationToken);
                _logger.LogInformation("Company context initialized to: {Company} (Id: {Id})", resolvedName, ActiveCompanyId);
            }
            else
            {
                ActiveCompanyName = "No Company Selected";
                ActiveCompanyId = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while initializing active company context");
        }
        finally
        {
            _lock.Release();
        }

        if (!string.IsNullOrEmpty(resolvedName))
        {
            ActiveCompanyChanged?.Invoke(this, resolvedName);
        }

        return resolvedName;
    }

    public async Task EnsureActiveCompanyAvailableAsync(CancellationToken cancellationToken = default)
    {
        var companies = await _auditRepository.GetAllCompaniesAsync(cancellationToken);
        if (companies.Count == 0)
        {
            await InitializeCompanyContextAsync(cancellationToken);
        }
    }
}
