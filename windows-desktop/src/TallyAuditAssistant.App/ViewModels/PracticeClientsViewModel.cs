using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Practice;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

/// <summary>Lazy-loaded CA practice workspace for client and legal-entity records.</summary>
public partial class PracticeClientsViewModel : ObservableObject, INavigationAware
{
    private readonly IPracticeRepository _repository;

    [ObservableProperty] private PracticeClient? _selectedClient;
    [ObservableProperty] private string _clientCode = string.Empty;
    [ObservableProperty] private string _clientName = string.Empty;
    [ObservableProperty] private string _clientType = "Business";
    [ObservableProperty] private string _clientPan = string.Empty;
    [ObservableProperty] private string _clientEmail = string.Empty;
    [ObservableProperty] private string _clientPhone = string.Empty;
    [ObservableProperty] private string _entityLegalName = string.Empty;
    [ObservableProperty] private string _entityPan = string.Empty;
    [ObservableProperty] private string _entityGstin = string.Empty;
    [ObservableProperty] private string _entityStateCode = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<PracticeClient> Clients { get; } = new();
    public ObservableCollection<PracticeClientEntity> Entities { get; } = new();
    public bool HasSelectedClient => SelectedClient != null;

    public PracticeClientsViewModel(IPracticeRepository repository) => _repository = repository;

    partial void OnSelectedClientChanged(PracticeClient? value)
    {
        OnPropertyChanged(nameof(HasSelectedClient));
        _ = LoadEntitiesAsync();
    }

    public async Task OnNavigatedToAsync() => await LoadClientsAsync();

    [RelayCommand]
    public async Task LoadClientsAsync()
    {
        IsLoading = true;
        try
        {
            var selectedId = SelectedClient?.Id;
            var rows = await _repository.GetClientsAsync();
            Clients.Clear();
            foreach (var row in rows) Clients.Add(row);
            SelectedClient = Clients.FirstOrDefault(c => c.Id == selectedId) ?? Clients.FirstOrDefault();
            StatusMessage = $"{Clients.Count} active practice client(s)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load clients: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task SaveClientAsync()
    {
        try
        {
            var client = new PracticeClient
            {
                ClientCode = ClientCode.Trim(),
                DisplayName = ClientName.Trim(),
                ClientType = string.IsNullOrWhiteSpace(ClientType) ? "Business" : ClientType.Trim(),
                PAN = NullIfWhiteSpace(ClientPan),
                Email = NullIfWhiteSpace(ClientEmail),
                Phone = NullIfWhiteSpace(ClientPhone)
            };
            await _repository.CreateClientAsync(client);
            ClientCode = ClientName = ClientPan = ClientEmail = ClientPhone = string.Empty;
            await LoadClientsAsync();
            SelectedClient = Clients.FirstOrDefault(c => c.Id == client.Id);
            StatusMessage = "Client saved.";
        }
        catch (Exception ex) { StatusMessage = $"Client was not saved: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task SaveEntityAsync()
    {
        if (SelectedClient == null) return;
        try
        {
            var entity = new PracticeClientEntity
            {
                ClientId = SelectedClient.Id,
                LegalName = EntityLegalName.Trim(),
                PAN = NullIfWhiteSpace(EntityPan),
                GSTIN = NullIfWhiteSpace(EntityGstin),
                StateCode = NullIfWhiteSpace(EntityStateCode)
            };
            await _repository.CreateClientEntityAsync(entity);
            EntityLegalName = EntityPan = EntityGstin = EntityStateCode = string.Empty;
            await LoadEntitiesAsync();
            StatusMessage = "Legal entity saved.";
        }
        catch (Exception ex) { StatusMessage = $"Entity was not saved: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task LoadEntitiesAsync()
    {
        Entities.Clear();
        if (SelectedClient == null) return;
        try
        {
            var rows = await _repository.GetClientEntitiesAsync(SelectedClient.Id);
            foreach (var row in rows) Entities.Add(row);
        }
        catch (Exception ex) { StatusMessage = $"Could not load legal entities: {ex.Message}"; }
    }

    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
