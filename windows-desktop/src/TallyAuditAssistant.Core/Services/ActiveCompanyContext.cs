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
    private FinancialPeriod? _currentPeriod;
    public event EventHandler<Company?>? ActiveCompanyChanged;

    public string? ActiveCompanyName => _currentCompany?.TallyCompanyName;
    public string? TallyCompanyName => _currentCompany?.TallyCompanyName;
    public string? ActiveCompanyId => _currentCompany?.Id;
    public Company? CurrentCompany => _currentCompany;
    public FinancialPeriod? CurrentPeriod => _currentPeriod ?? (_currentCompany != null ? CreatePeriodForCompany(_currentCompany) : null);
    public string? ActiveFinancialYear => CurrentPeriod?.FinancialYear;
    public string? ActiveFinancialPeriodId => CurrentPeriod?.FinancialPeriodId;
    public DateTime? ActivePeriodFrom => CurrentPeriod?.StartDate;
    public DateTime? ActivePeriodTo => CurrentPeriod?.EndDate;
    public DateTime? BooksFrom => CurrentPeriod?.StartDate;

    private static FinancialPeriod CreatePeriodForCompany(Company company)
    {
        var startDate = company.BooksFromDate != default ? company.BooksFromDate : new DateTime(DateTime.Today.Month < 4 ? DateTime.Today.Year - 1 : DateTime.Today.Year, 4, 1);
        var endDate = startDate.AddYears(1).AddDays(-1);
        return new FinancialPeriod
        {
            Id = $"{company.Id}-FY{startDate.Year}",
            CompanyId = company.Id,
            StartDate = startDate,
            EndDate = endDate
        };
    }

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
        var isMock = await _settingsService.IsMockModeEnabledAsync();

        if (_currentCompany != null)
        {
            if (!isMock && _currentCompany.IsMock)
            {
                _currentCompany = null;
                _currentPeriod = null;
            }
            else
            {
                if (_currentPeriod == null)
                {
                    _currentPeriod = CreatePeriodForCompany(_currentCompany);
                }
                return _currentCompany;
            }
        }

        var persistedName = await _settingsService.GetSettingAsync("ActiveCompany", string.Empty, cancellationToken);
        if (!string.IsNullOrEmpty(persistedName))
        {
            var comp = await _repository.GetCompanyByNameAsync(persistedName, cancellationToken)
                       ?? await _repository.GetCompanyByIdAsync(persistedName, cancellationToken);
            if (comp != null)
            {
                if (!isMock && comp.IsMock)
                {
                    await _settingsService.SetSettingAsync("ActiveCompany", string.Empty, cancellationToken);
                }
                else
                {
                    _currentCompany = comp;
                    _currentPeriod = CreatePeriodForCompany(_currentCompany);
                    return _currentCompany;
                }
            }
            else if (!isMock)
            {
                var compObj = new Company
                {
                    Id = persistedName,
                    TallyCompanyName = persistedName,
                    FormalName = persistedName,
                    BooksFromDate = new DateTime(DateTime.Today.Month < 4 ? DateTime.Today.Year - 1 : DateTime.Today.Year, 4, 1),
                    IsActive = true,
                    IsMock = false,
                    CreatedAt = DateTime.UtcNow
                };
                _currentCompany = compObj;
                _currentPeriod = CreatePeriodForCompany(compObj);
                try
                {
                    await _repository.EnsureCompanyAsync(compObj, cancellationToken);
                }
                catch { }
                return _currentCompany;
            }
        }

        if (isMock)
        {
            return await EnsureAndInitializeActiveCompanyAsync(cancellationToken);
        }

        return null;
    }

    public async Task<FinancialPeriod?> GetActivePeriodAsync(CancellationToken cancellationToken = default)
    {
        if (_currentPeriod != null)
        {
            return _currentPeriod;
        }

        var comp = await GetActiveCompanyAsync(cancellationToken);
        if (comp == null) return null;

        _currentPeriod = CreatePeriodForCompany(comp);
        return _currentPeriod;
    }

    public async Task ClearActiveCompanyAsync(CancellationToken cancellationToken = default)
    {
        _currentCompany = null;
        _currentPeriod = null;

        await _settingsService.SetSettingAsync("ActiveCompany", string.Empty, cancellationToken);
        await _settingsService.SetSettingAsync("FinancialYear", string.Empty, cancellationToken);
        await _settingsService.SetSettingAsync("FinancialPeriodId", string.Empty, cancellationToken);
        await _settingsService.SetSettingAsync("AuditPeriodFrom", string.Empty, cancellationToken);
        await _settingsService.SetSettingAsync("AuditPeriodTo", string.Empty, cancellationToken);

        ActiveCompanyChanged?.Invoke(this, null);
    }

    public async Task SetActiveCompanyAsync(Company company, CancellationToken cancellationToken = default)
    {
        if (company == null)
        {
            await ClearActiveCompanyAsync(cancellationToken);
            return;
        }

        _currentCompany = company;
        _currentPeriod = CreatePeriodForCompany(company);

        await _settingsService.SetSettingAsync("ActiveCompany", company.TallyCompanyName, cancellationToken);
        
        var fy = _currentPeriod.FinancialYear;
        await _settingsService.SetSettingAsync("FinancialYear", fy, cancellationToken);
        await _settingsService.SetSettingAsync("FinancialPeriodId", _currentPeriod.FinancialPeriodId, cancellationToken);
        await _settingsService.SetSettingAsync("AuditPeriodFrom", _currentPeriod.StartDate.ToString("yyyy-MM-dd"), cancellationToken);
        await _settingsService.SetSettingAsync("AuditPeriodTo", _currentPeriod.EndDate.ToString("yyyy-MM-dd"), cancellationToken);

        ActiveCompanyChanged?.Invoke(this, _currentCompany);
    }

    public async Task SetActiveCompanyNameAsync(string companyName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyName))
        {
            await ClearActiveCompanyAsync(cancellationToken);
            return;
        }

        var trimmedName = companyName.Trim();
        var isMock = await _settingsService.IsMockModeEnabledAsync();

        var existing = await _repository.GetCompanyByNameAsync(trimmedName, cancellationToken)
                       ?? await _repository.GetCompanyByIdAsync(trimmedName, cancellationToken);

        if (existing != null)
        {
            await SetActiveCompanyAsync(existing, cancellationToken);
            return;
        }

        var company = new Company
        {
            Id = trimmedName,
            TallyCompanyName = trimmedName,
            FormalName = trimmedName,
            BooksFromDate = new DateTime(DateTime.Today.Month < 4 ? DateTime.Today.Year - 1 : DateTime.Today.Year, 4, 1),
            LastSyncDate = isMock ? DateTime.UtcNow : null,
            LastAlterId = isMock ? 10042 : 0,
            IsActive = true,
            IsMock = isMock,
            CreatedAt = DateTime.UtcNow
        };

        // Set active immediately so UI header and SyncViewModel are instantly populated
        await SetActiveCompanyAsync(company, cancellationToken);

        // Best-effort profile fetch and repository persistence
        try
        {
            var host = await _settingsService.GetTallyHostAsync();
            var port = await _settingsService.GetTallyPortAsync();
            var endpointUrl = (!string.IsNullOrEmpty(host) && port > 0) ? $"http://{host}:{port}" : null;
            var profile = await _companyService.GetCompanyProfileTypedAsync(trimmedName, endpointUrl, cancellationToken);
            if (profile != null)
            {
                company.Id = profile.Name ?? trimmedName;
                company.TallyCompanyName = profile.Name ?? trimmedName;
                company.FormalName = profile.FormalName ?? trimmedName;
                company.GSTIN = profile.GSTIN;
                company.PAN = profile.PAN;
                company.StateName = profile.StateName;
                company.StateCode = profile.StateCode;
                company.BooksFromDate = profile.BooksBeginningFrom;
                company.LastAlterId = profile.AlterId;
            }
        }
        catch
        {
            // Non-blocking profile enrichment
        }

        try
        {
            var saved = await _repository.EnsureCompanyAsync(company, cancellationToken);
            _currentCompany = saved;
            _currentPeriod = CreatePeriodForCompany(saved);
        }
        catch
        {
            // Retain memory state if database is busy
        }
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
                var existingPersisted = await _repository.GetCompanyByNameAsync(persistedName, cancellationToken)
                                        ?? await _repository.GetCompanyByIdAsync(persistedName, cancellationToken);
                if (existingPersisted != null && !existingPersisted.IsMock)
                {
                    targetCompanyName = existingPersisted.TallyCompanyName;
                }
                else
                {
                    await _settingsService.SetSettingAsync("ActiveCompany", string.Empty, cancellationToken);
                }
            }

            if (string.IsNullOrEmpty(targetCompanyName) && !isMock)
            {
                targetCompanyName = await _companyService.GetActiveCompanyAsync(null, cancellationToken);
            }

            if (string.IsNullOrEmpty(targetCompanyName))
            {
                if (!isMock)
                {
                    // Check if repository has any real companies
                    var all = await _repository.GetAllCompaniesAsync(cancellationToken);
                    var realCompanies = all.Where(c => !c.IsMock).ToList();
                    if (realCompanies.Count > 0)
                    {
                        _currentCompany = realCompanies[0];
                        await SetActiveCompanyAsync(_currentCompany, cancellationToken);
                        return _currentCompany;
                    }
                    return null;
                }
            }

            if (string.IsNullOrEmpty(targetCompanyName))
            {
                return null;
            }

            var dbCompany = await _repository.GetCompanyByNameAsync(targetCompanyName, cancellationToken)
                            ?? await _repository.GetCompanyByIdAsync(targetCompanyName, cancellationToken);

            if (!isMock && dbCompany != null && dbCompany.IsMock)
            {
                return null;
            }

            var profile = await _companyService.GetCompanyProfileTypedAsync(targetCompanyName, null, cancellationToken);

            var companyToEnsure = new Company
            {
                Id = dbCompany?.Id ?? profile?.Name ?? targetCompanyName,
                TallyCompanyName = profile?.Name ?? dbCompany?.TallyCompanyName ?? targetCompanyName,
                FormalName = profile?.FormalName ?? dbCompany?.FormalName ?? targetCompanyName,
                GSTIN = profile?.GSTIN ?? dbCompany?.GSTIN,
                PAN = profile?.PAN ?? dbCompany?.PAN,
                StateName = profile?.StateName ?? dbCompany?.StateName,
                StateCode = profile?.StateCode ?? dbCompany?.StateCode,
                BooksFromDate = profile?.BooksBeginningFrom ?? dbCompany?.BooksFromDate ?? new DateTime(2025, 4, 1),
                LastSyncDate = dbCompany?.LastSyncDate ?? (isMock ? DateTime.UtcNow : null),
                LastAlterId = profile?.AlterId ?? dbCompany?.LastAlterId ?? (isMock ? 10042 : 0),
                IsActive = true,
                IsMock = isMock,
                CreatedAt = dbCompany?.CreatedAt ?? DateTime.UtcNow
            };

            var saved = await _repository.EnsureCompanyAsync(companyToEnsure, cancellationToken);
            _currentCompany = saved;
            _currentPeriod = CreatePeriodForCompany(saved);

            await _settingsService.SetSettingAsync("ActiveCompany", saved.TallyCompanyName, cancellationToken);
            var fy = _currentPeriod.FinancialYear;
            await _settingsService.SetSettingAsync("FinancialYear", fy, cancellationToken);
            await _settingsService.SetSettingAsync("FinancialPeriodId", _currentPeriod.FinancialPeriodId, cancellationToken);
            await _settingsService.SetSettingAsync("AuditPeriodFrom", _currentPeriod.StartDate.ToString("yyyy-MM-dd"), cancellationToken);
            await _settingsService.SetSettingAsync("AuditPeriodTo", _currentPeriod.EndDate.ToString("yyyy-MM-dd"), cancellationToken);

            ActiveCompanyChanged?.Invoke(this, _currentCompany);
            return _currentCompany;
        }
        catch
        {
            return null;
        }
    }
}
