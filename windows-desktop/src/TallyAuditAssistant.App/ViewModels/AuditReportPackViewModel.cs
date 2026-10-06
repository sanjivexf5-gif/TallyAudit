using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class AuditReportPackViewModel : ObservableObject, INavigationAware
{
    private readonly IAuditRepository _auditRepository;
    private readonly IAuditFinalizationRepository _finalizationRepository;
    private readonly IActiveCompanyContext _companyContext;
    private readonly IAuditTrailService? _auditTrailService;
    private string _companyId = string.Empty;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _activeCompanyName = "No Company Selected";
    [ObservableProperty] private string _financialYear = string.Empty;
    [ObservableProperty] private int _riskScore;
    [ObservableProperty] private string _riskLevel = "LOW";
    [ObservableProperty] private int _totalFindings;
    [ObservableProperty] private int _criticalFindings;
    [ObservableProperty] private int _highFindings;
    [ObservableProperty] private int _mediumFindings;
    [ObservableProperty] private int _lowFindings;
    [ObservableProperty] private int _pendingFindings;
    [ObservableProperty] private int _reviewedFindings;
    [ObservableProperty] private int _checklistCompletion;
    [ObservableProperty] private int _totalChecklist;
    [ObservableProperty] private int _completedChecklist;
    [ObservableProperty] private int _totalEvidence;
    [ObservableProperty] private int _verifiedEvidence;
    [ObservableProperty] private int _rejectedEvidence;
    [ObservableProperty] private string _finalizationStatus = "In Progress";
    [ObservableProperty] private string _conclusionStatus = "No Exceptions Noted";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isSuccess;

    public ObservableCollection<string> PackSections { get; } = new()
    {
        "Executive Summary",
        "Risk & Findings Analysis",
        "Audit Checklist",
        "Audit Evidence",
        "Audit Documentation",
        "Finalization & Conclusion",
        "Read-Only TallyPrime Statement"
    };

    public AuditReportPackViewModel(
        IAuditRepository auditRepository,
        IAuditFinalizationRepository finalizationRepository,
        IActiveCompanyContext companyContext,
        IAuditTrailService? auditTrailService = null)
    {
        _auditRepository = auditRepository;
        _finalizationRepository = finalizationRepository;
        _companyContext = companyContext;
        _auditTrailService = auditTrailService;
        _companyContext.ActiveCompanyChanged += (_, _) => _ = LoadAsync();
        _ = LoadAsync();
    }

    public async Task OnNavigatedToAsync() => await LoadAsync();

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
                Reset();
                StatusMessage = "Select a company to build the audit pack.";
                return;
            }

            _companyId = company.Id;
            ActiveCompanyName = company.TallyCompanyName;
            FinancialYear = ResolveFinancialYear(DateTime.Today);

            var exceptions = (await _auditRepository.GetExceptionsAsync(company.Id, take: 5000)).ToList();
            TotalFindings = exceptions.Count;
            CriticalFindings = exceptions.Count(x => x.Severity == SeverityLevel.Critical);
            HighFindings = exceptions.Count(x => x.Severity == SeverityLevel.High);
            MediumFindings = exceptions.Count(x => x.Severity == SeverityLevel.Medium);
            LowFindings = exceptions.Count(x => x.Severity == SeverityLevel.Low);
            PendingFindings = exceptions.Count(x => x.Status == ReviewStatus.Pending);
            ReviewedFindings = exceptions.Count(x => x.Status == ReviewStatus.Reviewed || x.Status == ReviewStatus.Resolved);
            RiskScore = Math.Min(100, CriticalFindings * 30 + HighFindings * 15 + MediumFindings * 7 + LowFindings * 3);
            RiskLevel = RiskScore >= 75 ? "CRITICAL" : RiskScore >= 50 ? "HIGH" : RiskScore >= 25 ? "MEDIUM" : "LOW";

            var checklist = (await _finalizationRepository.GetChecklistAsync($"CHECKLIST-{company.Id}")).ToList();
            TotalChecklist = checklist.Count;
            CompletedChecklist = checklist.Count(x => x.Status == "Completed");
            var applicable = checklist.Count(x => x.Status != "Not Applicable");
            ChecklistCompletion = applicable == 0 ? 100 : (int)Math.Round(CompletedChecklist * 100.0 / applicable);

            var planId = await _finalizationRepository.GetOrCreateWorkingPaperPlanIdAsync(company.Id, company.TallyCompanyName);
            var evidence = (await _finalizationRepository.GetAuditEvidenceAsync(planId)).ToList();
            TotalEvidence = evidence.Count;
            VerifiedEvidence = evidence.Count(x => string.Equals(x.Status, "Verified", StringComparison.OrdinalIgnoreCase));
            RejectedEvidence = evidence.Count(x => string.Equals(x.Status, "Rejected", StringComparison.OrdinalIgnoreCase));

            var state = await _finalizationRepository.GetStateAsync(company.Id, $"FY-{FinancialYear}");
            if (state != null)
            {
                FinalizationStatus = state.Status.ToString();
                ConclusionStatus = state.AuditorConclusionStatus;
            }

            StatusMessage = "Audit pack data is ready.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to load audit pack: {ex.Message}";
            IsError = true;
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task GeneratePackAsync()
    {
        IsLoading = true;
        IsSuccess = IsError = false;
        try
        {
            var company = await _companyContext.GetActiveCompanyAsync();
            if (company == null) throw new InvalidOperationException("Select a company first.");

            await LoadAsync();

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TallyAuditReportPacks");
            Directory.CreateDirectory(dir);
            var safe = string.Join("_", company.TallyCompanyName.Split(Path.GetInvalidFileNameChars()));
            var file = Path.Combine(dir, $"Audit_Report_Pack_{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.html");

            string E(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
            var html = new StringBuilder();
            html.AppendLine("<!doctype html><html><head><meta charset='utf-8'><title>Audit Report Pack</title>");
            html.AppendLine("<style>body{font-family:Segoe UI,Arial;margin:42px;color:#172033;line-height:1.5}h1{text-align:center;margin-bottom:4px}h2{margin-top:30px;border-bottom:2px solid #dbe2ea;padding-bottom:6px}.meta{text-align:center;color:#64748b}.cards{display:flex;gap:12px;flex-wrap:wrap}.card{border:1px solid #dbe2ea;border-radius:8px;padding:14px;min-width:150px}.big{font-size:25px;font-weight:700}table{border-collapse:collapse;width:100%;margin-top:12px}th,td{border:1px solid #ccd3df;padding:8px;text-align:left}th{background:#f1f5f9}.note{background:#f8fafc;border-left:4px solid #2563eb;padding:12px}.footer{margin-top:42px;color:#64748b;font-size:12px}</style></head><body>");
            html.AppendLine($"<h1>Audit Report Pack</h1><div class='meta'><b>{E(company.TallyCompanyName)}</b> · Financial Year {E(FinancialYear)}<br>Generated {DateTime.Now:dd MMM yyyy HH:mm}</div>");

            html.AppendLine("<h2>1. Executive Summary</h2><div class='cards'>");
            html.AppendLine($"<div class='card'><div>Risk Score</div><div class='big'>{RiskScore}/100</div><b>{E(RiskLevel)}</b></div>");
            html.AppendLine($"<div class='card'><div>Total Findings</div><div class='big'>{TotalFindings}</div><div>{PendingFindings} pending</div></div>");
            html.AppendLine($"<div class='card'><div>Checklist</div><div class='big'>{ChecklistCompletion}%</div><div>{CompletedChecklist}/{TotalChecklist} completed</div></div>");
            html.AppendLine($"<div class='card'><div>Evidence</div><div class='big'>{VerifiedEvidence}/{TotalEvidence}</div><div>verified</div></div></div>");

            html.AppendLine("<h2>2. Risk & Findings Analysis</h2><table><tr><th>Severity</th><th>Count</th></tr>");
            html.AppendLine($"<tr><td>Critical</td><td>{CriticalFindings}</td></tr><tr><td>High</td><td>{HighFindings}</td></tr><tr><td>Medium</td><td>{MediumFindings}</td></tr><tr><td>Low</td><td>{LowFindings}</td></tr><tr><td>Pending</td><td>{PendingFindings}</td></tr><tr><td>Reviewed / Resolved</td><td>{ReviewedFindings}</td></tr></table>");

            html.AppendLine("<h2>3. Audit Checklist</h2><p>Completion: <b>" + ChecklistCompletion + "%</b>. " + CompletedChecklist + " of " + TotalChecklist + " configured procedures are completed.</p>");
            html.AppendLine("<h2>4. Audit Evidence</h2><table><tr><th>Metric</th><th>Count</th></tr>");
            html.AppendLine($"<tr><td>Total evidence</td><td>{TotalEvidence}</td></tr><tr><td>Verified</td><td>{VerifiedEvidence}</td></tr><tr><td>Rejected</td><td>{RejectedEvidence}</td></tr></table>");

            html.AppendLine("<h2>5. Audit Documentation</h2><div class='note'>This pack is designed to accompany the application's Working Papers, Audit Evidence Register and Management Representation Letter. Those documents remain separately available in the application and can be retained with this pack.</div>");

            html.AppendLine("<h2>6. Finalization & Conclusion</h2><table><tr><th>Field</th><th>Value</th></tr>");
            html.AppendLine($"<tr><td>Finalization status</td><td>{E(FinalizationStatus)}</td></tr><tr><td>Conclusion</td><td>{E(ConclusionStatus)}</td></tr></table>");

            html.AppendLine("<h2>7. Read-Only TallyPrime Statement</h2><div class='note'>Tally Audit Assistant reads and analyzes synchronized TallyPrime data. This report pack does not modify, post, alter or write back any TallyPrime transaction or master data.</div>");
            html.AppendLine("<div class='footer'>Generated by Tally Audit Assistant. This report is an audit working-paper aid and does not constitute statutory certification, legal advice, or a substitute for professional audit judgement.</div></body></html>");

            await File.WriteAllTextAsync(file, html.ToString(), Encoding.UTF8);

            if (_auditTrailService != null)
                await _auditTrailService.RecordActivityAsync(
                    actionType: "Audit report pack generated",
                    module: "AUDIT REPORTING",
                    description: $"Board-ready audit report pack generated for {company.TallyCompanyName}.",
                    companyName: company.TallyCompanyName,
                    ct: CancellationToken.None);

            StatusMessage = $"Audit report pack generated: {file}";
            IsSuccess = true;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = file, UseShellExecute = true }); } catch { }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Report pack generation failed: {ex.Message}";
            IsError = true;
        }
        finally { IsLoading = false; }
    }

    private void Reset()
    {
        _companyId = string.Empty;
        ActiveCompanyName = "No Company Selected";
        TotalFindings = CriticalFindings = HighFindings = MediumFindings = LowFindings = PendingFindings = ReviewedFindings = 0;
        RiskScore = ChecklistCompletion = TotalChecklist = CompletedChecklist = TotalEvidence = VerifiedEvidence = RejectedEvidence = 0;
        FinalizationStatus = "In Progress";
        ConclusionStatus = "No Exceptions Noted";
    }

    private static string ResolveFinancialYear(DateTime date) =>
        date.Month >= 4 ? $"{date.Year}-{(date.Year + 1) % 100:D2}" : $"{date.Year - 1}-{date.Year % 100:D2}";
}
