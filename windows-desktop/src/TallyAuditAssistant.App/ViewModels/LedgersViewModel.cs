using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dapper;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Ledgers;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.App.ViewModels;

public partial class LedgersViewModel : ObservableObject, INavigationAware
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ISettingsService _settingsService;
    private readonly IAuditRepository _repository;
    private readonly IActiveCompanyContext _companyContext;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _activeCompanyName = string.Empty;

    [ObservableProperty]
    private string _financialYear = "FY 2025-26";

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _hasLedgers = false;

    public ObservableCollection<Ledger> Ledgers { get; } = new();

    public LedgersViewModel(
        SqliteConnectionFactory connectionFactory,
        ISettingsService settingsService,
        IAuditRepository repository,
        IActiveCompanyContext companyContext,
        INavigationService navigationService)
    {
        _connectionFactory = connectionFactory;
        _settingsService = settingsService;
        _repository = repository;
        _companyContext = companyContext;
        _navigationService = navigationService;

        _companyContext.ActiveCompanyChanged += OnActiveCompanyChanged;
        _ = LoadLedgersAsync();
    }

    public async Task OnNavigatedToAsync()
    {
        await LoadLedgersAsync();
    }

    private void OnActiveCompanyChanged(object? sender, Company? comp)
    {
        _ = LoadLedgersAsync();
    }

    [RelayCommand]
    public async Task LoadLedgersAsync()
    {
        IsLoading = true;
        try
        {
            var comp = await _companyContext.GetActiveCompanyAsync();
            if (comp == null)
            {
                ActiveCompanyName = "No Company Selected";
                FinancialYear = "—";
                Ledgers.Clear();
                HasLedgers = false;
                return;
            }

            var period = await _companyContext.GetActivePeriodAsync();
            var current = comp;

            ActiveCompanyName = current.TallyCompanyName;

            if (period != null)
            {
                FinancialYear = period.FinancialYear;
            }
            else if (current.BooksFromDate != default)
            {
                var year = current.BooksFromDate.Year;
                FinancialYear = $"FY {year}-{(year + 1) % 100:D2}";
            }

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
            HasLedgers = Ledgers.Count > 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void GoToSync()
    {
        _navigationService.Navigate("Sync");
    }

    async partial void OnSearchQueryChanged(string value) => await LoadLedgersAsync();
}
