using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Dapper;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Ledgers;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.App.ViewModels;

public partial class LedgersViewModel : ObservableObject
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ISettingsService _settingsService;
    private readonly IAuditRepository _repository;
    private readonly IActiveCompanyContext _companyContext;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _activeCompanyName = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public ObservableCollection<Ledger> Ledgers { get; } = new();

    public LedgersViewModel(
        SqliteConnectionFactory connectionFactory,
        ISettingsService settingsService,
        IAuditRepository repository,
        IActiveCompanyContext companyContext)
    {
        _connectionFactory = connectionFactory;
        _settingsService = settingsService;
        _repository = repository;
        _companyContext = companyContext;

        _companyContext.ActiveCompanyChanged += OnActiveCompanyChanged;
        _ = LoadLedgersAsync();
    }

    private void OnActiveCompanyChanged(object? sender, Company? comp)
    {
        _ = LoadLedgersAsync();
    }

    public async Task LoadLedgersAsync()
    {
        IsLoading = true;
        try
        {
            var comp = await _companyContext.GetActiveCompanyAsync()
                       ?? await _companyContext.EnsureAndInitializeActiveCompanyAsync();
            var activeName = comp?.TallyCompanyName ?? await _settingsService.GetSettingAsync("ActiveCompany", string.Empty);
            ActiveCompanyName = activeName;

            var companies = await _repository.GetAllCompaniesAsync();
            if (companies.Count == 0)
            {
                Ledgers.Clear();
                return;
            }

            var current = (comp != null ? companies.FirstOrDefault(c => c.Id == comp.Id || c.TallyCompanyName == comp.TallyCompanyName) : null)
                          ?? (string.IsNullOrEmpty(activeName) ? companies[0] : (companies.FirstOrDefault(c => c.TallyCompanyName == activeName) ?? companies[0]));

            ActiveCompanyName = current.TallyCompanyName;

            using var connection = await _connectionFactory.CreateConnectionAsync();
            var sql = "SELECT * FROM Ledgers WHERE CompanyId = @CompanyId";
            var parameters = new DynamicParameters();
            parameters.Add("CompanyId", current.Id);

            if (!string.IsNullOrEmpty(SearchQuery))
            {
                sql += " AND (Name LIKE @Query OR ParentGroup LIKE @Query OR GSTIN LIKE @Query OR PAN LIKE @Query)";
                parameters.Add("Query", $"%{SearchQuery}%");
            }

            sql += " ORDER BY Name ASC LIMIT 100";

            var list = await connection.QueryAsync<Ledger>(sql, parameters);
            Ledgers.Clear();
            foreach (var l in list)
            {
                Ledgers.Add(l);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    async partial void OnSearchQueryChanged(string value) => await LoadLedgersAsync();
}
