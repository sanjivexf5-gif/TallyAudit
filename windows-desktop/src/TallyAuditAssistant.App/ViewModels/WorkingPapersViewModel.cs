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

public partial class WorkingPapersViewModel : ObservableObject, INavigationAware
{
    private readonly IAuditFinalizationRepository _repository;
    private readonly IActiveCompanyContext _companyContext;
    private readonly IAuditTrailService? _auditTrailService;
    private string _planId = string.Empty;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _activeCompanyName = "No Company Selected";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isSuccess;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private WorkingPaper? _selectedWorkingPaper;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _auditAreaFilter = "All";
    [ObservableProperty] private string _statusFilter = "All";
    [ObservableProperty] private string _titleInput = string.Empty;
    [ObservableProperty] private string _auditAreaInput = "General";
    [ObservableProperty] private string _objectiveInput = string.Empty;
    [ObservableProperty] private string _procedureInput = string.Empty;
    [ObservableProperty] private string _conclusionInput = string.Empty;
    [ObservableProperty] private string _workingPaperStatusInput = "Draft";
    [ObservableProperty] private string _relatedFindingIdInput = string.Empty;
    [ObservableProperty] private string _preparedByInput = Environment.UserName;

    public ObservableCollection<WorkingPaper> WorkingPapers { get; } = new();
    public ObservableCollection<WorkingPaperAttachment> Attachments { get; } = new();

    public WorkingPapersViewModel(IAuditFinalizationRepository repository, IActiveCompanyContext companyContext, IAuditTrailService? auditTrailService = null)
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
        IsLoading = true;
        IsError = false;
        try
        {
            var company = await _companyContext.GetActiveCompanyAsync();
            if (company == null)
            {
                ActiveCompanyName = "No Company Selected";
                WorkingPapers.Clear();
                SelectedWorkingPaper = null;
                _planId = string.Empty;
                return;
            }

            ActiveCompanyName = company.TallyCompanyName;
            _planId = await _repository.GetOrCreateWorkingPaperPlanIdAsync(company.Id, company.TallyCompanyName);
            var items = await _repository.GetWorkingPapersAsync(_planId);
            var filtered = items.Where(x =>
                (string.IsNullOrWhiteSpace(SearchQuery) || x.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                 x.Objective.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || x.Conclusion.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) &&
                (AuditAreaFilter == "All" || x.AuditArea == AuditAreaFilter) &&
                (StatusFilter == "All" || x.Status == StatusFilter))
                .OrderByDescending(x => x.PreparedDate).ThenBy(x => x.Title).ToList();

            WorkingPapers.Clear();
            foreach (var item in filtered) WorkingPapers.Add(item);
            if (SelectedWorkingPaper != null) SelectedWorkingPaper = WorkingPapers.FirstOrDefault(x => x.Id == SelectedWorkingPaper.Id);
            if (SelectedWorkingPaper == null && WorkingPapers.Count > 0) SelectedWorkingPaper = WorkingPapers[0];
            StatusMessage = $"{WorkingPapers.Count} working paper(s) available.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to load working papers: {ex.Message}";
            IsError = true;
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public void NewWorkingPaper()
    {
        SelectedWorkingPaper = null;
        TitleInput = string.Empty; AuditAreaInput = "General"; ObjectiveInput = string.Empty;
        ProcedureInput = string.Empty; ConclusionInput = string.Empty; WorkingPaperStatusInput = "Draft";
        RelatedFindingIdInput = string.Empty; PreparedByInput = Environment.UserName; Attachments.Clear();
        StatusMessage = "New working paper ready.";
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_planId)) { StatusMessage = "Select and synchronize a company first."; IsError = true; return; }
        if (string.IsNullOrWhiteSpace(TitleInput)) { StatusMessage = "Working paper title is required."; IsError = true; return; }

        IsLoading = true; IsError = false; IsSuccess = false;
        try
        {
            var company = await _companyContext.GetActiveCompanyAsync();
            if (company == null) return;
            var paper = SelectedWorkingPaper ?? new WorkingPaper();
            paper.PlanId = _planId; paper.CompanyId = company.Id; paper.CompanyName = company.TallyCompanyName;
            paper.AuditArea = string.IsNullOrWhiteSpace(AuditAreaInput) ? "General" : AuditAreaInput.Trim();
            paper.Title = TitleInput.Trim(); paper.Objective = ObjectiveInput.Trim();
            paper.ProcedurePerformed = ProcedureInput.Trim(); paper.Conclusion = ConclusionInput.Trim();
            paper.Status = string.IsNullOrWhiteSpace(WorkingPaperStatusInput) ? "Draft" : WorkingPaperStatusInput;
            paper.PreparedBy = string.IsNullOrWhiteSpace(PreparedByInput) ? Environment.UserName : PreparedByInput.Trim();
            paper.RelatedFindingId = string.IsNullOrWhiteSpace(RelatedFindingIdInput) ? null : RelatedFindingIdInput.Trim();

            await _repository.SaveWorkingPaperAsync(paper);
            if (_auditTrailService != null)
                await _auditTrailService.RecordActivityAsync("Working paper saved", "WORKING PAPERS",
                    $"Working paper '{paper.Title}' saved with status '{paper.Status}'.", company.TallyCompanyName, CancellationToken.None);

            await LoadAsync();
            SelectedWorkingPaper = WorkingPapers.FirstOrDefault(x => x.Id == paper.Id);
            StatusMessage = "Working paper saved successfully."; IsSuccess = true;
        }
        catch (Exception ex) { StatusMessage = $"Save failed: {ex.Message}"; IsError = true; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task AddAttachmentAsync()
    {
        if (SelectedWorkingPaper == null) { StatusMessage = "Save the working paper before adding evidence."; IsError = true; return; }

        var dialog = new OpenFileDialog
        {
            Title = "Select audit evidence", Multiselect = true,
            Filter = "Supported files|*.pdf;*.xlsx;*.xls;*.csv;*.png;*.jpg;*.jpeg;*.docx;*.txt|All files|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TallyAuditWorkingPapers", Sanitize(ActiveCompanyName), SelectedWorkingPaper.Id);
            Directory.CreateDirectory(root);

            foreach (var source in dialog.FileNames)
            {
                var target = Path.Combine(root, Path.GetFileName(source));
                if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    File.Copy(source, target, true);
                await _repository.SaveWorkingPaperAttachmentAsync(new WorkingPaperAttachment
                {
                    WorkingPaperId = SelectedWorkingPaper.Id, FileName = Path.GetFileName(target), FilePath = target,
                    FileSizeBytes = new FileInfo(target).Length, FileHash = await ComputeSha256Async(target),
                    AddedBy = Environment.UserName, AddedAt = DateTime.UtcNow
                });
            }
            await LoadAttachmentsAsync();
            StatusMessage = $"{dialog.FileNames.Length} evidence file(s) added."; IsSuccess = true;
        }
        catch (Exception ex) { StatusMessage = $"Evidence attachment failed: {ex.Message}"; IsError = true; }
    }

    [RelayCommand]
    public void OpenAttachment(WorkingPaperAttachment? attachment)
    {
        if (attachment == null || !File.Exists(attachment.FilePath)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(attachment.FilePath) { UseShellExecute = true }); }
        catch (Exception ex) { StatusMessage = $"Unable to open evidence: {ex.Message}"; IsError = true; }
    }

    [RelayCommand]
    public void OpenEvidenceFolder()
    {
        if (SelectedWorkingPaper == null) return;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TallyAuditWorkingPapers", Sanitize(ActiveCompanyName), SelectedWorkingPaper.Id);
        Directory.CreateDirectory(dir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
    }

    partial void OnSelectedWorkingPaperChanged(WorkingPaper? value)
    {
        if (value == null)
        {
            TitleInput = string.Empty; AuditAreaInput = "General"; ObjectiveInput = string.Empty;
            ProcedureInput = string.Empty; ConclusionInput = string.Empty; WorkingPaperStatusInput = "Draft";
            RelatedFindingIdInput = string.Empty; Attachments.Clear(); return;
        }
        TitleInput = value.Title; AuditAreaInput = value.AuditArea; ObjectiveInput = value.Objective;
        ProcedureInput = value.ProcedurePerformed; ConclusionInput = value.Conclusion; WorkingPaperStatusInput = value.Status;
        RelatedFindingIdInput = value.RelatedFindingId ?? string.Empty; PreparedByInput = value.PreparedBy;
        _ = LoadAttachmentsAsync();
    }

    private async Task LoadAttachmentsAsync()
    {
        Attachments.Clear();
        if (SelectedWorkingPaper == null) return;
        foreach (var item in await _repository.GetWorkingPaperAttachmentsAsync(SelectedWorkingPaper.Id)) Attachments.Add(item);
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