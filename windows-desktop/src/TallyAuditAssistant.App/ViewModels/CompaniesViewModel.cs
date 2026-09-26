using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class CompaniesViewModel : ObservableObject
{
    private readonly IAuditRepository _repository;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private Company? _selectedCompany;

    [ObservableProperty]
    private string _activeCompanyName = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<Company> Companies { get; } = new();

    public CompaniesViewModel(IAuditRepository repository, ISettingsService settingsService)
    {
        _repository = repository;
        _settingsService = settingsService;
        _ = LoadCompaniesAsync();
    }

    public async Task LoadCompaniesAsync()
    {
        IsLoading = true;
        try
        {
            var activeName = await _settingsService.GetSettingAsync("ActiveCompany", "Apex Industrial Solutions Pvt Ltd");
            ActiveCompanyName = activeName;

            var list = await _repository.GetAllCompaniesAsync();
            Companies.Clear();
            foreach (var c in list)
            {
                Companies.Add(c);
            }

            SelectedCompany = list.FirstOrDefault(c => c.TallyCompanyName == activeName) ?? list.FirstOrDefault();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SelectCompanyAsync()
    {
        if (SelectedCompany == null) return;
        await _settingsService.SetSettingAsync("ActiveCompany", SelectedCompany.TallyCompanyName);
        ActiveCompanyName = SelectedCompany.TallyCompanyName;
    }
}
