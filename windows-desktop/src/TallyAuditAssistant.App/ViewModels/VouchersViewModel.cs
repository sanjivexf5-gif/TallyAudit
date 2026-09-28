using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dapper;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Vouchers;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.App.ViewModels;

public partial class VouchersViewModel : ObservableObject, INavigationAware
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
    private bool _hasVouchers = false;

    public ObservableCollection<Voucher> Vouchers { get; } = new();

    public VouchersViewModel(
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
        _ = LoadVouchersAsync();
    }

    public async Task OnNavigatedToAsync()
    {
        await LoadVouchersAsync();
    }

    private void OnActiveCompanyChanged(object? sender, Company? comp)
    {
        _ = LoadVouchersAsync();
    }

    [RelayCommand]
    public async Task LoadVouchersAsync()
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
                Vouchers.Clear();
                HasVouchers = false;
                return;
            }

            var current = (comp != null ? companies.FirstOrDefault(c => c.Id == comp.Id || c.TallyCompanyName == comp.TallyCompanyName) : null)
                          ?? (string.IsNullOrEmpty(activeName) ? companies[0] : (companies.FirstOrDefault(c => c.TallyCompanyName == activeName) ?? companies[0]));

            ActiveCompanyName = current.TallyCompanyName;

            if (current.BooksFrom.HasValue)
            {
                var year = current.BooksFrom.Value.Year;
                FinancialYear = $"FY {year}-{(year + 1) % 100:D2}";
            }

            using var connection = await _connectionFactory.CreateConnectionAsync();
            var sql = "SELECT * FROM Vouchers WHERE CompanyId = @CompanyId";
            var parameters = new DynamicParameters();
            parameters.Add("CompanyId", current.Id);

            if (!string.IsNullOrEmpty(SearchQuery))
            {
                sql += " AND (VoucherNumber LIKE @Query OR VoucherTypeName LIKE @Query OR PartyLedgerName LIKE @Query OR Narration LIKE @Query)";
                parameters.Add("Query", $"%{SearchQuery}%");
            }

            sql += " ORDER BY VoucherDate DESC, VoucherNumber ASC LIMIT 100";

            var list = await connection.QueryAsync<Voucher>(sql, parameters);
            Vouchers.Clear();
            foreach (var v in list)
            {
                Vouchers.Add(v);
            }
            HasVouchers = Vouchers.Count > 0;
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

    async partial void OnSearchQueryChanged(string value) => await LoadVouchersAsync();
}
