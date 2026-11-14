using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Practice;
using TallyAuditAssistant.Core.Domain.Security;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

/// <summary>CA service engagement register with client/entity scope, ownership, review and due-date tracking.</summary>
public partial class PracticeEngagementsViewModel : ObservableObject, INavigationAware
{
    private readonly IPracticeRepository _practice;
    private readonly IUserRepository _users;

    [ObservableProperty] private PracticeClient? _selectedClient;
    [ObservableProperty] private PracticeClientEntity? _selectedEntity;
    [ObservableProperty] private ClientEngagement? _selectedEngagement;
    [ObservableProperty] private string _serviceCode = "GST-RETURN";
    [ObservableProperty] private string _serviceName = "GST Return Preparation";
    [ObservableProperty] private string _financialYear = "2026-27";
    [ObservableProperty] private string _status = "Open";
    [ObservableProperty] private string _dueDateText = string.Empty;
    [ObservableProperty] private string _professionalFeeText = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private AppUser? _assignedUser;
    [ObservableProperty] private AppUser? _reviewerUser;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<PracticeClient> Clients { get; } = new();
    public ObservableCollection<PracticeClientEntity> Entities { get; } = new();
    public ObservableCollection<ClientEngagement> Engagements { get; } = new();
    public ObservableCollection<AppUser> ActiveUsers { get; } = new();
    public IReadOnlyList<string> StatusOptions { get; } = new[] { "Open", "In Progress", "Awaiting Client", "Under Review", "Completed", "On Hold", "Cancelled" };
    public bool HasSelectedClient => SelectedClient != null;

    public PracticeEngagementsViewModel(IPracticeRepository practice, IUserRepository users)
    {
        _practice = practice;
        _users = users;
    }

    public async Task OnNavigatedToAsync() => await LoadAsync();

    partial void OnSelectedClientChanged(PracticeClient? value)
    {
        OnPropertyChanged(nameof(HasSelectedClient));
        _ = LoadEntitiesAsync();
        _ = LoadEngagementsAsync();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var previousId = SelectedClient?.Id;
            var clientsTask = _practice.GetClientsAsync();
            var usersTask = _users.GetAllAsync();
            await Task.WhenAll(clientsTask, usersTask);
            Clients.Clear();
            foreach (var client in clientsTask.Result) Clients.Add(client);
            ActiveUsers.Clear();
            foreach (var user in usersTask.Result.Where(u => u.IsActive).OrderBy(u => u.DisplayName)) ActiveUsers.Add(user);
            SelectedClient = Clients.FirstOrDefault(c => c.Id == previousId) ?? Clients.FirstOrDefault();
            await LoadEngagementsAsync();
            StatusMessage = $"{Engagements.Count} engagement(s) loaded";
        }
        catch (Exception ex) { StatusMessage = $"Could not load engagements: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task LoadEntitiesAsync()
    {
        Entities.Clear();
        SelectedEntity = null;
        if (SelectedClient == null) return;
        try
        {
            foreach (var entity in await _practice.GetClientEntitiesAsync(SelectedClient.Id))
                if (entity.Status == "Active") Entities.Add(entity);
        }
        catch (Exception ex) { StatusMessage = $"Could not load client entities: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task LoadEngagementsAsync()
    {
        Engagements.Clear();
        if (SelectedClient == null) return;
        try
        {
            foreach (var item in await _practice.GetEngagementsAsync(SelectedClient.Id))
                Engagements.Add(item);
        }
        catch (Exception ex) { StatusMessage = $"Could not load engagements: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task SaveEngagementAsync()
    {
        if (SelectedClient == null) { StatusMessage = "Select a practice client first."; return; }
        if (!DateTime.TryParseExact(DueDateText, new[] { "dd-MM-yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dueDate)
            && !string.IsNullOrWhiteSpace(DueDateText))
        { StatusMessage = "Enter due date as DD-MM-YYYY."; return; }
        decimal? fee = null;
        if (!string.IsNullOrWhiteSpace(ProfessionalFeeText))
        {
            if (!decimal.TryParse(ProfessionalFeeText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedFee) || parsedFee < 0)
            { StatusMessage = "Enter a valid non-negative professional fee."; return; }
            fee = parsedFee;
        }

        try
        {
            var engagement = new ClientEngagement
            {
                ClientId = SelectedClient.Id,
                ClientEntityId = SelectedEntity?.Id,
                ServiceCode = ServiceCode.Trim(),
                ServiceName = ServiceName.Trim(),
                FinancialYear = FinancialYear.Trim(),
                Status = Status,
                AssignedToUserId = AssignedUser?.Id,
                ReviewerUserId = ReviewerUser?.Id,
                DueDate = string.IsNullOrWhiteSpace(DueDateText) ? null : dueDate,
                ProfessionalFee = fee,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
            };
            await _practice.CreateEngagementAsync(engagement);
            ServiceCode = "GST-RETURN";
            ServiceName = "GST Return Preparation";
            DueDateText = ProfessionalFeeText = Notes = string.Empty;
            AssignedUser = ReviewerUser = null;
            await LoadEngagementsAsync();
            StatusMessage = "Engagement created. This records work status only; it does not file a statutory return.";
        }
        catch (Exception ex) { StatusMessage = $"Engagement was not saved: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task UpdateSelectedStatusAsync()
    {
        if (SelectedEngagement == null) { StatusMessage = "Select an engagement to update."; return; }
        try
        {
            SelectedEngagement.Status = Status;
            var saved = await _practice.UpdateEngagementAsync(SelectedEngagement);
            StatusMessage = saved ? "Engagement status updated." : "Engagement could not be updated.";
            await LoadEngagementsAsync();
        }
        catch (Exception ex) { StatusMessage = $"Status update failed: {ex.Message}"; }
    }
}
