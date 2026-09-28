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
    public string? ActiveCompanyId => _currentCompany?.Id;
    public Company? CurrentCompany => _currentCompany;
    public FinancialPeriod? CurrentPeriod => _currentPeriod ?? (_currentCompany != null ? CreatePeriodForCompany(_currentCompany) : null);
    public string? ActiveFinancialYear => CurrentPeriod?.FinancialYear;
    public string? ActiveFinancialPeriodId => CurrentPeriod?.FinancialPeriodId;
    public DateTime? ActivePeriodFrom => CurrentPeriod?.StartDate;
    public DateTime? ActivePeriodTo => CurrentPeriod?.EndDate;

    private static FinancialPeriod CreatePeriodForCompany(Company company)
    {
        var startDate = company.BooksFromDate != default ? company.BooksFromDate : new DateTime(2025, 4, 1);
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
        if (_currentCompany != null)
        {
            if (_currentPeriod == null)
            {
                _currentPeriod = CreatePeriodForCompany(_currentCompany);
            }
            return _currentCompany;
        }

        var persistedName = await _settingsService.GetSettingAsync("ActiveCompany", string.Empty, cancellationToken);
        if (!string.IsNullOrEmpty(persistedName))
        {
            _currentCompany = await _repository.GetCompanyByNameAsync(persistedName, cancellationToken)
                              ?? await _repository.GetCompanyByIdAsync(persistedName, cancellationToken);
            if (_currentCompany != null)
            {
                _currentPeriod = CreatePeriodForCompany(_currentCompany);
                return _currentCompany;
            }
        }

        return await EnsureAndInitializeActiveCompanyAsync(cancellationToken);
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

    public async Task SetActiveCompanyAsync(Company company, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);

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
