using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Core.Services;

public class ActiveCompanyContext : IActiveCompanyContext
{
    private readonly IAuditRepository _repository;
    private readonly ISettingsService _settingsService;
    private readonly ITallyCompanyService _companyService;
    private readonly SemaphoreSlim _lock = new(1, 1);

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
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await GetActiveCompanyInternalAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Company?> GetActiveCompanyInternalAsync(CancellationToken cancellationToken)
    {
        var isMock = await _settingsService.IsMockModeEnabledAsync();

        if (_currentCompany != null)
        {
            if (!isMock && _currentCompany.IsMock)
            {
                _currentCompany.IsMock = false;
                _currentCompany.IsActive = true;
                try
                {
                    await _repository.EnsureCompanyAsync(_currentCompany, cancellationToken);
                }
                catch { }
            }

            if (_currentPeriod == null)
            {
                _currentPeriod = CreatePeriodForCompany(_currentCompany);
            }
            return _currentCompany;
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
                    comp.IsMock = false;
                    comp.IsActive = true;
                    try
                    {
                        await _repository.EnsureCompanyAsync(comp, cancellationToken);
                    }
                    catch { }
                }

                _currentCompany = comp;
                _currentPeriod = CreatePeriodForCompany(_currentCompany);
                return _currentCompany;
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
            return await EnsureAndInitializeActiveCompanyInternalAsync(cancellationToken);
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
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await ClearActiveCompanyInternalAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ClearActiveCompanyInternalAsync(CancellationToken cancellationToken)
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
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await SetActiveCompanyInternalAsync(company, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SetActiveCompanyInternalAsync(Company company, CancellationToken cancellationToken)
    {
        if (company == null || string.IsNullOrWhiteSpace(company.TallyCompanyName))
        {
            await ClearActiveCompanyInternalAsync(cancellationToken);
            return;
        }

        var isMock = await _settingsService.IsMockModeEnabledAsync();
        if (!isMock && company.IsMock)
        {
            company.IsMock = false;
            company.IsActive = true;
            try
            {
                await _repository.EnsureCompanyAsync(company, cancellationToken);
            }
            catch { }
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

    public async Task<Company> ReconcileLiveTallyCompanyAsync(string companyName, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await ReconcileLiveTallyCompanyInternalAsync(companyName, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Company> ReconcileLiveTallyCompanyInternalAsync(string companyName, CancellationToken cancellationToken)
    {
        var trimmedName = companyName.Trim();
        var isMock = await _settingsService.IsMockModeEnabledAsync();

        var existing = await _repository.GetCompanyByNameAsync(trimmedName, cancellationToken)
                       ?? await _repository.GetCompanyByIdAsync(trimmedName, cancellationToken);

        Company companyToSave;
        if (existing != null)
        {
            existing.TallyCompanyName = trimmedName;
            existing.FormalName = string.IsNullOrWhiteSpace(existing.FormalName) ? trimmedName : existing.FormalName;
            existing.IsActive = true;
            existing.IsMock = isMock;
            companyToSave = existing;
        }
        else
        {
            companyToSave = new Company
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
        }

        // Non-blocking profile enrichment (strictly ensure non-empty profile name)
        try
        {
            var host = await _settingsService.GetTallyHostAsync();
            var port = await _settingsService.GetTallyPortAsync();
            var endpointUrl = (!string.IsNullOrEmpty(host) && port > 0) ? $"http://{host}:{port}" : null;
            var profile = await _companyService.GetCompanyProfileTypedAsync(trimmedName, endpointUrl, cancellationToken);
            if (profile != null && !string.IsNullOrWhiteSpace(profile.Name))
            {
                companyToSave.Id = profile.Name;
                companyToSave.TallyCompanyName = profile.Name;
                companyToSave.FormalName = string.IsNullOrWhiteSpace(profile.FormalName) ? profile.Name : profile.FormalName;
                companyToSave.GSTIN = profile.GSTIN;
                companyToSave.PAN = profile.PAN;
                companyToSave.StateName = profile.StateName;
                companyToSave.StateCode = profile.StateCode;
                companyToSave.BooksFromDate = profile.BooksBeginningFrom;
                companyToSave.LastAlterId = profile.AlterId;
            }
        }
        catch
        {
            // Non-blocking profile enrichment
        }

        var saved = await _repository.EnsureCompanyAsync(companyToSave, cancellationToken);
        if (saved == null)
        {
            throw new InvalidOperationException($"Failed to persist company record for '{trimmedName}'. Repository returned null.");
        }
        return saved;
    }

    public async Task SetActiveCompanyNameAsync(string companyName, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(companyName))
            {
                await ClearActiveCompanyInternalAsync(cancellationToken);
                return;
            }

            var trimmedName = companyName.Trim();
            var reconciledCompany = await ReconcileLiveTallyCompanyInternalAsync(trimmedName, cancellationToken);

            await SetActiveCompanyInternalAsync(reconciledCompany, cancellationToken);
            await VerifyActivationInternalAsync(trimmedName, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task VerifyActivationInternalAsync(string trimmedName, CancellationToken cancellationToken)
    {
        var persistedName = await _settingsService.GetSettingAsync("ActiveCompany", string.Empty, cancellationToken);
        if (!string.Equals(persistedName?.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Active company persistence verification failed. Expected '{trimmedName}', persisted '{persistedName}'.");
        }

        var verified = await GetActiveCompanyInternalAsync(cancellationToken);
        if (verified == null || !string.Equals(verified.TallyCompanyName, trimmedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Active company verification failed. Expected '{trimmedName}', but the active company context did not resolve correctly.");
        }

        var isMock = await _settingsService.IsMockModeEnabledAsync();
        var dbCompany = await _repository.GetCompanyByNameAsync(trimmedName, cancellationToken)
                        ?? await _repository.GetCompanyByIdAsync(trimmedName, cancellationToken);
        if (!isMock && dbCompany != null && dbCompany.IsMock)
        {
            throw new InvalidOperationException(
                $"Active company database verification failed. Expected IsMock=false for '{trimmedName}', but found IsMock=true.");
        }
    }

    public async Task<Company?> EnsureAndInitializeActiveCompanyAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await EnsureAndInitializeActiveCompanyInternalAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Company?> EnsureAndInitializeActiveCompanyInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_currentCompany != null && !string.IsNullOrWhiteSpace(_currentCompany.TallyCompanyName))
            {
                return _currentCompany;
            }

            var isMock = await _settingsService.IsMockModeEnabledAsync();
            var persistedName = await _settingsService.GetSettingAsync("ActiveCompany", string.Empty, cancellationToken);

            string? targetCompanyName = null;

            if (isMock)
            {
                targetCompanyName = await _companyService.GetActiveCompanyAsync(null, cancellationToken);
            }
            else if (!string.IsNullOrEmpty(persistedName))
            {
                var existingPersisted = await _repository.GetCompanyByNameAsync(persistedName, cancellationToken)
                                        ?? await _repository.GetCompanyByIdAsync(persistedName, cancellationToken);
                if (existingPersisted != null)
                {
                    if (existingPersisted.IsMock)
                    {
                        existingPersisted.IsMock = false;
                        existingPersisted.IsActive = true;
                        try
                        {
                            await _repository.EnsureCompanyAsync(existingPersisted, cancellationToken);
                        }
                        catch { }
                    }
                    targetCompanyName = existingPersisted.TallyCompanyName;
                }
                else
                {
                    targetCompanyName = persistedName;
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
                    var all = await _repository.GetAllCompaniesAsync(cancellationToken);
                    var realCompanies = all.Where(c => !c.IsMock).ToList();
                    if (realCompanies.Count > 0)
                    {
                        _currentCompany = realCompanies[0];
                        await SetActiveCompanyInternalAsync(_currentCompany, cancellationToken);
                        return _currentCompany;
                    }
                    return null;
                }
                return null;
            }

            var dbCompany = await _repository.GetCompanyByNameAsync(targetCompanyName, cancellationToken)
                            ?? await _repository.GetCompanyByIdAsync(targetCompanyName, cancellationToken);

            TallyCompanyProfile? profile = null;
            try
            {
                var host = await _settingsService.GetTallyHostAsync();
                var port = await _settingsService.GetTallyPortAsync();
                var endpointUrl = (!string.IsNullOrEmpty(host) && port > 0) ? $"http://{host}:{port}" : null;
                profile = await _companyService.GetCompanyProfileTypedAsync(targetCompanyName, endpointUrl, cancellationToken);
            }
            catch
            {
                // Non-blocking profile enrichment during startup initialization
            }

            var companyToEnsure = new Company
            {
                Id = dbCompany?.Id ?? profile?.Name ?? targetCompanyName,
                TallyCompanyName = profile?.Name ?? dbCompany?.TallyCompanyName ?? targetCompanyName,
                FormalName = profile?.FormalName ?? dbCompany?.FormalName ?? targetCompanyName,
                GSTIN = profile?.GSTIN ?? dbCompany?.GSTIN,
                PAN = profile?.PAN ?? dbCompany?.PAN,
                StateName = profile?.StateName ?? dbCompany?.StateName,
                StateCode = profile?.StateCode ?? dbCompany?.StateCode,
                BooksFromDate = profile?.BooksBeginningFrom ?? dbCompany?.BooksFromDate ?? new DateTime(DateTime.Today.Month < 4 ? DateTime.Today.Year - 1 : DateTime.Today.Year, 4, 1),
                LastSyncDate = dbCompany?.LastSyncDate ?? (isMock ? DateTime.UtcNow : null),
                LastAlterId = profile?.AlterId ?? dbCompany?.LastAlterId ?? (isMock ? 10042 : 0),
                IsActive = true,
                IsMock = isMock,
                CreatedAt = dbCompany?.CreatedAt ?? DateTime.UtcNow
            };

            var saved = await _repository.EnsureCompanyAsync(companyToEnsure, cancellationToken);
            _currentCompany = saved ?? companyToEnsure;
            _currentPeriod = CreatePeriodForCompany(_currentCompany);

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
