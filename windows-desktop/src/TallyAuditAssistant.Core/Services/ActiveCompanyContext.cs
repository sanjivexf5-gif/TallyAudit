using System;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Core.Services;

public class ActiveCompanyContext : IActiveCompanyContext
{
    private readonly IAuditRepository _repository;
    private readonly ISettingsService _settingsService;
    private readonly ITallyCompanyService _companyService;

    private Company? _currentCompany;
    public event EventHandler<Company?>? ActiveCompanyChanged;

    public string? ActiveCompanyName => _currentCompany?.TallyCompanyName;
    public string? ActiveCompanyId => _currentCompany?.Id;
    public Company? CurrentCompany => _currentCompany;

    public ActiveCompanyContext(
        IAuditRepository repository,
        ISettingsService settingsService,
        ITallyCompanyService companyService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _companyService = companyService ?? throw new ArgumentNullException(nameof(companyService));
    }

    public async Task<Company?> GetActiveCompanyAsync(CancellationToken cancellationToken = default)
    {
        if (_currentCompany != null)
        {
            return _currentCompany;
        }

        var persistedName = await _settingsService.GetSettingAsync("ActiveCompany", string.Empty, cancellationToken);
        if (!string.IsNullOrEmpty(persistedName))
        {
            _currentCompany = await _repository.GetCompanyByNameAsync(persistedName, cancellationToken)
                              ?? await _repository.GetCompanyByIdAsync(persistedName, cancellationToken);
            if (_currentCompany != null)
            {
                return _currentCompany;
            }
        }

        return await EnsureAndInitializeActiveCompanyAsync(cancellationToken);
    }

    public async Task SetActiveCompanyAsync(Company company, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);

        _currentCompany = company;
        await _settingsService.SetSettingAsync("ActiveCompany", company.TallyCompanyName, cancellationToken);
        
        var fy = $"FY {company.BooksFromDate.Year}-{(company.BooksFromDate.Year + 1) % 100:D2}";
        await _settingsService.SetSettingAsync("FinancialYear", fy, cancellationToken);
        await _settingsService.SetSettingAsync("AuditPeriodFrom", company.BooksFromDate.ToString("yyyy-MM-dd"), cancellationToken);
        await _settingsService.SetSettingAsync("AuditPeriodTo", company.BooksFromDate.AddYears(1).AddDays(-1).ToString("yyyy-MM-dd"), cancellationToken);

        ActiveCompanyChanged?.Invoke(this, _currentCompany);
    }

    public async Task SetActiveCompanyNameAsync(string companyName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyName)) return;

        var existing = await _repository.GetCompanyByNameAsync(companyName, cancellationToken)
                       ?? await _repository.GetCompanyByIdAsync(companyName, cancellationToken);

        if (existing != null)
        {
            await SetActiveCompanyAsync(existing, cancellationToken);
            return;
        }

        var profile = await _companyService.GetCompanyProfileTypedAsync(companyName, null, cancellationToken);
        var company = new Company
        {
            Id = profile?.Name ?? companyName,
            TallyCompanyName = profile?.Name ?? companyName,
            FormalName = profile?.FormalName ?? companyName,
            GSTIN = profile?.GSTIN,
            PAN = profile?.PAN,
            StateName = profile?.StateName,
            StateCode = profile?.StateCode,
            BooksFromDate = profile?.BooksBeginningFrom ?? new DateTime(2025, 4, 1),
            LastSyncDate = DateTime.UtcNow,
            LastAlterId = profile?.AlterId ?? 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var saved = await _repository.EnsureCompanyAsync(company, cancellationToken);
        await SetActiveCompanyAsync(saved, cancellationToken);
    }

    public async Task<Company?> EnsureAndInitializeActiveCompanyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var isMock = await _settingsService.IsMockModeEnabledAsync();
            var persistedName = await _settingsService.GetSettingAsync("ActiveCompany", string.Empty, cancellationToken);

            string? targetCompanyName = null;

            if (isMock)
            {
                // In mock mode, resolve from DynamicTallyCompanyService (which delegates to MockTallyCompanyService)
                targetCompanyName = await _companyService.GetActiveCompanyAsync(null, cancellationToken);
            }
            else if (!string.IsNullOrEmpty(persistedName))
            {
                targetCompanyName = persistedName;
            }
            else
            {
                targetCompanyName = await _companyService.GetActiveCompanyAsync(null, cancellationToken);
            }

            if (string.IsNullOrEmpty(targetCompanyName))
            {
                // Check if repository has any existing companies
                var all = await _repository.GetAllCompaniesAsync(cancellationToken);
                if (all.Count > 0)
                {
                    _currentCompany = all[0];
                    await SetActiveCompanyAsync(_currentCompany, cancellationToken);
                    return _currentCompany;
                }
                return null;
            }

            var dbCompany = await _repository.GetCompanyByNameAsync(targetCompanyName, cancellationToken)
                            ?? await _repository.GetCompanyByIdAsync(targetCompanyName, cancellationToken);

            var profile = await _companyService.GetCompanyProfileTypedAsync(targetCompanyName, null, cancellationToken);

            var companyToEnsure = new Company
            {
                Id = dbCompany?.Id ?? targetCompanyName,
                TallyCompanyName = profile?.Name ?? targetCompanyName,
                FormalName = profile?.FormalName ?? dbCompany?.FormalName ?? targetCompanyName,
                GSTIN = profile?.GSTIN ?? dbCompany?.GSTIN,
                PAN = profile?.PAN ?? dbCompany?.PAN,
                StateName = profile?.StateName ?? dbCompany?.StateName,
                StateCode = profile?.StateCode ?? dbCompany?.StateCode,
                BooksFromDate = profile?.BooksBeginningFrom ?? dbCompany?.BooksFromDate ?? new DateTime(2025, 4, 1),
                LastSyncDate = dbCompany?.LastSyncDate ?? DateTime.UtcNow,
                LastAlterId = profile?.AlterId ?? dbCompany?.LastAlterId ?? 10042,
                IsActive = true,
                CreatedAt = dbCompany?.CreatedAt ?? DateTime.UtcNow
            };

            var saved = await _repository.EnsureCompanyAsync(companyToEnsure, cancellationToken);
            _currentCompany = saved;

            await _settingsService.SetSettingAsync("ActiveCompany", saved.TallyCompanyName, cancellationToken);
            var fy = $"FY {saved.BooksFromDate.Year}-{(saved.BooksFromDate.Year + 1) % 100:D2}";
            await _settingsService.SetSettingAsync("FinancialYear", fy, cancellationToken);

            ActiveCompanyChanged?.Invoke(this, _currentCompany);
            return _currentCompany;
        }
        catch
        {
            return null;
        }
    }
}
