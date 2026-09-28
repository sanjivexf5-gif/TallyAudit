using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class CompaniesViewModel : ObservableObject, INavigationAware
{
    private readonly IAuditRepository _repository;
    private readonly ISettingsService _settingsService;
    private readonly IActiveCompanyContext _companyContext;
    private readonly ITallyCompanyService _companyService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private Company? _selectedCompany;

    [ObservableProperty]
    private string _activeCompanyName = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasCompanies;

    public ObservableCollection<Company> Companies { get; } = new();

    public CompaniesViewModel(
        IAuditRepository repository,
        ISettingsService settingsService,
        IActiveCompanyContext companyContext,
        ITallyCompanyService companyService,
        INavigationService navigationService)
    {
        _repository = repository;
        _settingsService = settingsService;
        _companyContext = companyContext;
        _companyService = companyService;
        _navigationService = navigationService;

        _companyContext.ActiveCompanyChanged += OnActiveCompanyChanged;
        _ = LoadCompaniesAsync();
    }

    public async Task OnNavigatedToAsync()
    {
        await LoadCompaniesAsync();
    }

    private void OnActiveCompanyChanged(object? sender, Company? comp)
    {
        if (comp == null) return;
        ActiveCompanyName = comp.TallyCompanyName;
        var match = Companies.FirstOrDefault(c => c.TallyCompanyName == comp.TallyCompanyName || c.Id == comp.Id);
        if (match != null)
        {
            SelectedCompany = match;
        }
    }

    [RelayCommand]
    public async Task LoadCompaniesAsync()
    {
        IsLoading = true;
        try
        {
            var currentActive = await _companyContext.GetActiveCompanyAsync()
                                ?? await _companyContext.EnsureAndInitializeActiveCompanyAsync();

            var list = await _repository.GetAllCompaniesAsync();
            Companies.Clear();
            foreach (var c in list)
            {
                Companies.Add(c);
            }

            HasCompanies = Companies.Count > 0;

            var activeName = currentActive?.TallyCompanyName
                             ?? _companyContext.ActiveCompanyName
                             ?? await _settingsService.GetSettingAsync("ActiveCompany", string.Empty);

            ActiveCompanyName = !string.IsNullOrEmpty(activeName) ? activeName : (list.FirstOrDefault()?.TallyCompanyName ?? string.Empty);

            SelectedCompany = list.FirstOrDefault(c => c.TallyCompanyName == ActiveCompanyName || c.Id == currentActive?.Id)
                              ?? list.FirstOrDefault();
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
        await _companyContext.SetActiveCompanyAsync(SelectedCompany);
        ActiveCompanyName = SelectedCompany.TallyCompanyName;
    }

    [RelayCommand]
    private async Task RefreshCompaniesAsync()
    {
        await LoadCompaniesAsync();
    }

    [RelayCommand]
    public void GoToSync()
    {
        _navigationService.Navigate("Sync");
    }
}
