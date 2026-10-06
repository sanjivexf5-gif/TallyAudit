using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class AuditEvidenceViewModel : ObservableObject, INavigationAware
{
    private readonly IAuditFinalizationRepository _repository;
    private readonly IActiveCompanyContext _companyContext;
    private readonly IAuditTrailService? _auditTrailService;
    private string _planId = string.Empty;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _activeCompanyName = "No Company Selected";
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _areaFilter = "All";
    [ObservableProperty] private string _statusFilter = "All";
    [ObservableProperty] private AuditEvidence? _selectedEvidence;
    [ObservableProperty] private string _descriptionInput = string.Empty;
    [ObservableProperty] private string _auditAreaInput = "General";
    [ObservableProperty] private string _evidenceTypeInput = "Document";
    [ObservableProperty] private string _referenceNumberInput = string.Empty;
    [ObservableProperty] private string _findingIdInput = string.Empty;
    [ObservableProperty] private string _remarksInput = string.Empty;
    [ObservableProperty] private string _statusInput = "Received";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isSuccess;

    public ObservableCollection<AuditEvidence> Evidence { get; } = new();

    public int TotalCount => Evidence.Count;
    public int ReceivedCount => Evidence.Count(x => x.Status == "Received");
    public int VerifiedCount => Evidence.Count(x => x.Status == "Verified");
    public int RejectedCount => Evidence.Count(x => x.Status == "Rejected");

    public AuditEvidenceViewModel(IAuditFinalizationRepository repository, IActiveCompanyContext companyContext, IAuditTrailService? auditTrailService = null)
    {
        _repository = repository;
        _companyContext = companyContext;
        _auditTrailService = auditTrailService;
        _companyContext.ActiveCompanyChanged += OnActiveCompanyChanged;
        _ = LoadAsync();
    }

    public async Task OnNavigatedToAsync() => await LoadAsync();
    private void OnActiveCompanyChanged(object? sender, Company? company) => _ = LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true; IsError = false;
        try
        {
            var company = await _companyContext.GetActiveCompanyAsync();
            if (company == null)
            {
                ActiveCompanyName = "No Company Selected"; _planId = string.Empty; Evidence.Clear(); NotifyCounts(); return;
            }
            ActiveCompanyName = company.TallyCompanyName;
            _planId = await _repository.GetOrCreateWorkingPaperPlanIdAsync(company.Id, company.TallyCompanyName);
            var items = await _repository.GetAuditEvidenceAsync(_planId);
            var filtered = items.Where(x =>
                (string.IsNullOrWhiteSpace(SearchQuery) ||
                 x.Description.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                 x.ReferenceNumber.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                 (x.FileName?.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ?? false)) &&
                (AreaFilter == "All" || x.AuditArea == AreaFilter) &&
                (StatusFilter == "All" || x.Status == StatusFilter)).ToList();
            Evidence.Clear();
            foreach (var item in filtered) Evidence.Add(item);
            NotifyCounts();
            StatusMessage = $"{Evidence.Count} evidence item(s) available.";
        }
        catch (Exception ex) { StatusMessage = $"Unable to load evidence: {ex.Message}"; IsError = true; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public void NewEvidence()
    {
        SelectedEvidence = null; DescriptionInput = string.Empty; AuditAreaInput = "General";
        EvidenceTypeInput = "Document"; ReferenceNumberInput = string.Empty; FindingIdInput = string.Empty;
        RemarksInput = string.Empty; StatusInput = "Received"; StatusMessage = "New evidence item ready.";
    }

    [RelayCommand]
    public async Task AddFileAsync()
    {
        var dialog = new OpenFileDialog { Title = "Select audit evidence", Filter = "Supported files|*.pdf;*.xlsx;*.xls;*.csv;*.png;*.jpg;*.jpeg;*.docx;*.txt|All files|*.*" };
        if (dialog.ShowDialog() != true) return;
        var company = await _companyContext.GetActiveCompanyAsync();
        if (company == null || string.IsNullOrWhiteSpace(_planId)) { StatusMessage = "Select and synchronize a company first."; IsError = true; return; }

        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TallyAuditEvidence", Sanitize(company.TallyCompanyName));
            Directory.CreateDirectory(root);
            var source = dialog.FileName;
            var target = Path.Combine(root, $"{DateTime.Now:yyyyMMdd_HHmmss}_{Path.GetFileName(source)}");
            File.Copy(source, target, true);
            DescriptionInput = string.IsNullOrWhiteSpace(DescriptionInput) ? Path.GetFileName(source) : DescriptionInput;
            await SaveEvidenceCoreAsync(target);
        }
        catch (Exception ex) { StatusMessage = $"Evidence import failed: {ex.Message}"; IsError = true; }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        await SaveEvidenceCoreAsync(null);
    }

    private async Task SaveEvidenceCoreAsync(string? importedPath)
    {
        if (string.IsNullOrWhiteSpace(_planId)) { StatusMessage = "Select and synchronize a company first."; IsError = true; return; }
        if (string.IsNullOrWhiteSpace(DescriptionInput)) { StatusMessage = "Evidence description is required."; IsError = true; return; }
        IsLoading = true; IsError = false; IsSuccess = false;
        try
        {
            var company = await _companyContext.GetActiveCompanyAsync();
            if (company == null) return;
            var evidence = SelectedEvidence ?? new AuditEvidence();
            evidence.PlanId = _planId;
            evidence.AuditArea = string.IsNullOrWhiteSpace(AuditAreaInput) ? "General" : AuditAreaInput.Trim();
            evidence.EvidenceType = string.IsNullOrWhiteSpace(EvidenceTypeInput) ? "Document" : EvidenceTypeInput.Trim();
            evidence.Description = DescriptionInput.Trim();
            evidence.ReferenceNumber = ReferenceNumberInput.Trim();
            evidence.FindingId = string.IsNullOrWhiteSpace(FindingIdInput) ? null : FindingIdInput.Trim();
            evidence.Status = string.IsNullOrWhiteSpace(StatusInput) ? "Received" : StatusInput;
            evidence.AuditorRemarks = RemarksInput.Trim();
            evidence.UploadedAt = evidence.UploadedAt == default ? DateTime.UtcNow : evidence.UploadedAt;
            if (!string.IsNullOrWhiteSpace(importedPath))
            {
                evidence.FilePath = importedPath;
                evidence.FileName = Path.GetFileName(importedPath);
                evidence.FileSizeBytes = new FileInfo(importedPath).Length;
                evidence.FileHash = await ComputeSha256Async(importedPath);
            }
            await _repository.SaveAuditEvidenceAsync(evidence);
            if (_auditTrailService != null)
                await _auditTrailService.RecordActivityAsync(
                    actionType: "Audit evidence saved", module: "AUDIT EVIDENCE",
                    description: $"Evidence '{evidence.Description}' saved with status '{evidence.Status}'.",
                    companyName: company.TallyCompanyName, ct: CancellationToken.None);
            await LoadAsync();
            SelectedEvidence = Evidence.FirstOrDefault(x => x.Id == evidence.Id);
            StatusMessage = "Evidence saved successfully."; IsSuccess = true;
        }
        catch (Exception ex) { StatusMessage = $"Save failed: {ex.Message}"; IsError = true; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task SetStatusAsync(string status)
    {
        if (SelectedEvidence == null) return;
        StatusInput = status;
        await SaveAsync();
    }

    [RelayCommand]
    public void OpenEvidence()
    {
        if (SelectedEvidence?.FilePath == null || !File.Exists(SelectedEvidence.FilePath)) { StatusMessage = "Evidence file is not available at the stored path."; IsError = true; return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SelectedEvidence.FilePath) { UseShellExecute = true }); }
        catch (Exception ex) { StatusMessage = $"Unable to open evidence: {ex.Message}"; IsError = true; }
    }

    [RelayCommand]
    public void OpenEvidenceFolder()
    {
        if (SelectedEvidence?.FilePath == null) return;
        var dir = Path.GetDirectoryName(SelectedEvidence.FilePath);
        if (string.IsNullOrWhiteSpace(dir)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
    }

    partial void OnSelectedEvidenceChanged(AuditEvidence? value)
    {
        if (value == null) { DescriptionInput = string.Empty; AuditAreaInput = "General"; EvidenceTypeInput = "Document"; ReferenceNumberInput = string.Empty; FindingIdInput = string.Empty; RemarksInput = string.Empty; StatusInput = "Received"; return; }
        DescriptionInput = value.Description; AuditAreaInput = value.AuditArea; EvidenceTypeInput = value.EvidenceType;
        ReferenceNumberInput = value.ReferenceNumber; FindingIdInput = value.FindingId ?? string.Empty;
        RemarksInput = value.AuditorRemarks; StatusInput = value.Status;
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(TotalCount)); OnPropertyChanged(nameof(ReceivedCount));
        OnPropertyChanged(nameof(VerifiedCount)); OnPropertyChanged(nameof(RejectedCount));
    }

    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return string.IsNullOrWhiteSpace(value) ? "Company" : value.Trim();
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
}