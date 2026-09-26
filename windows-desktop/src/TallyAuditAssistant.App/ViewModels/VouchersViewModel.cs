using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Dapper;
using TallyAuditAssistant.Core.Domain.Vouchers;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.App.ViewModels;

public partial class VouchersViewModel : ObservableObject
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ISettingsService _settingsService;
    private readonly IAuditRepository _repository;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _activeCompanyName = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public ObservableCollection<Voucher> Vouchers { get; } = new();

    public VouchersViewModel(SqliteConnectionFactory connectionFactory, ISettingsService settingsService, IAuditRepository repository)
    {
        _connectionFactory = connectionFactory;
        _settingsService = settingsService;
        _repository = repository;
        _ = LoadVouchersAsync();
    }

    public async Task LoadVouchersAsync()
    {
        IsLoading = true;
        try
        {
            var activeName = await _settingsService.GetSettingAsync("ActiveCompany", "Apex Industrial Solutions Pvt Ltd");
            ActiveCompanyName = activeName;

            var companies = await _repository.GetAllCompaniesAsync();
            if (companies.Count == 0) return;
            var current = companies.FirstOrDefault(c => c.TallyCompanyName == activeName) ?? companies[0];

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
        }
        finally
        {
            IsLoading = false;
        }
    }

    async partial void OnSearchQueryChanged(string value) => await LoadVouchersAsync();
}
