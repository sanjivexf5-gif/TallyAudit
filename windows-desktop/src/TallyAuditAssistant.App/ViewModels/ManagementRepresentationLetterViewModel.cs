using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class ManagementRepresentationLetterViewModel : ObservableObject, INavigationAware
{
    private readonly IAuditRepository _repository;
    private readonly IActiveCompanyContext _companyContext;
    private readonly IAuditTrailService? _auditTrailService;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _activeCompanyName = "No Company Selected";
    [ObservableProperty] private string _financialYear = string.Empty;
    [ObservableProperty] private string _managementName = string.Empty;
    [ObservableProperty] private string _auditorName = string.Empty;
    [ObservableProperty] private string _letterDate = DateTime.Today.ToString("dd-MMM-yyyy");
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isSuccess;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private string _lastGeneratedFilePath = string.Empty;
    [ObservableProperty] private int _totalFindings;
    [ObservableProperty] private int _criticalFindings;
    [ObservableProperty] private int _highFindings;
    [ObservableProperty] private int _pendingFindings;

    public ManagementRepresentationLetterViewModel(
        IAuditRepository repository,
        IActiveCompanyContext companyContext,
        IAuditTrailService? auditTrailService = null)
    {
        _repository = repository;
        _companyContext = companyContext;
        _auditTrailService = auditTrailService;
        _companyContext.ActiveCompanyChanged += (_, _) => _ = LoadAsync();
        _ = LoadAsync();
    }

    public async Task OnNavigatedToAsync() => await LoadAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            var company = await _companyContext.GetActiveCompanyAsync();
            if (company == null)
            {
                ActiveCompanyName = "No Company Selected";
                TotalFindings = CriticalFindings = HighFindings = PendingFindings = 0;
                return;
            }

            ActiveCompanyName = company.TallyCompanyName;
            var exceptions = await _repository.GetExceptionsAsync(company.Id, take: 5000);
            TotalFindings = exceptions.Count;
            CriticalFindings = exceptions.Count(e => e.Severity == SeverityLevel.Critical);
            HighFindings = exceptions.Count(e => e.Severity == SeverityLevel.High);
            PendingFindings = exceptions.Count(e => e.Status == ReviewStatus.Pending);

            if (string.IsNullOrWhiteSpace(FinancialYear))
                FinancialYear = ResolveFinancialYear(DateTime.Today);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to load company audit summary: {ex.Message}";
            IsError = true;
        }
    }

    [RelayCommand]
    public async Task GenerateAsync()
    {
        IsLoading = true;
        IsSuccess = IsError = false;
        StatusMessage = "Preparing management representation letter...";

        try
        {
            var company = await _companyContext.GetActiveCompanyAsync();
            if (company == null)
            {
                StatusMessage = "Select and synchronize a company first.";
                IsError = true;
                return;
            }

            var exceptions = await _repository.GetExceptionsAsync(company.Id, take: 5000);
            var reviewed = exceptions.Count(e => e.Status == ReviewStatus.Reviewed || e.Status == ReviewStatus.Resolved);
            var gst = exceptions.Count(e => e.Category == RuleCategory.GST);
            var tds = exceptions.Count(e => e.Category == RuleCategory.TDS);
            var duplicates = exceptions.Count(e => e.Category == RuleCategory.DuplicateDetection);

            var companyName = H(company.TallyCompanyName);
            var fy = H(string.IsNullOrWhiteSpace(FinancialYear) ? ResolveFinancialYear(DateTime.Today) : FinancialYear);
            var management = H(string.IsNullOrWhiteSpace(ManagementName) ? "The Management" : ManagementName);
            var auditor = H(string.IsNullOrWhiteSpace(AuditorName) ? "The Auditor" : AuditorName);
            var date = H(string.IsNullOrWhiteSpace(LetterDate) ? DateTime.Today.ToString("dd-MMM-yyyy") : LetterDate);

            var html = new StringBuilder();
            html.AppendLine("<!doctype html><html><head><meta charset='utf-8'>");
            html.AppendLine("<title>Management Representation Letter</title>");
            html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:48px;color:#172033;line-height:1.55}h1{text-align:center;font-size:22px;margin-bottom:4px}h2{font-size:15px;margin-top:26px;border-bottom:1px solid #ccd3df;padding-bottom:6px}.meta{margin:20px 0;padding:14px;background:#f4f7fb;border:1px solid #d9e0ea}.summary{display:flex;gap:12px}.kpi{border:1px solid #d9e0ea;padding:12px;min-width:120px}.kpi b{display:block;font-size:20px}.note{font-size:11px;color:#5d6878}.signature{margin-top:55px}.line{display:inline-block;border-bottom:1px solid #333;width:260px}.footer{margin-top:40px;font-size:10px;color:#667085}</style></head><body>");
            html.AppendLine("<h1>MANAGEMENT REPRESENTATION LETTER</h1>");
            html.AppendLine($"<div style='text-align:center'><b>{companyName}</b><br>Financial Year {fy}</div>");
            html.AppendLine("<div class='meta'>");
            html.AppendLine($"<b>Date:</b> {date}<br><b>To:</b> {auditor}<br><b>From:</b> {management}<br><b>Subject:</b> Management representations for the audit of {companyName}");
            html.AppendLine("</div>");
            html.AppendLine("<p>We acknowledge our responsibility for the preparation and fair presentation of the financial information and for maintaining appropriate accounting records and internal controls. Based on the information available to us, we provide the following representations for the audit period stated above.</p>");
            html.AppendLine("<h2>Management Representations</h2><ol>");
            html.AppendLine("<li>All books, records, vouchers and information requested for the audit have been made available to the auditor.</li>");
            html.AppendLine("<li>Transactions recorded in the accounting system are, to the best of management's knowledge, complete and supported by appropriate documentation.</li>");
            html.AppendLine("<li>Known liabilities, commitments and material matters have been disclosed to the auditor.</li>");
            html.AppendLine("<li>Tax-related information, including GST and TDS records, has been made available for audit procedures.</li>");
            html.AppendLine("<li>Management has informed the auditor of known fraud, suspected fraud, irregularities and significant control matters, where applicable.</li>");
            html.AppendLine("<li>There are no material matters requiring disclosure that have been intentionally withheld from the auditor.</li>");
            html.AppendLine("</ol>");
            html.AppendLine("<h2>Audit Analysis Summary</h2><div class='summary'>");
            html.AppendLine($"<div class='kpi'><span>Total findings</span><b>{TotalFindings}</b></div><div class='kpi'><span>Critical</span><b>{CriticalFindings}</b></div><div class='kpi'><span>High</span><b>{HighFindings}</b></div><div class='kpi'><span>Pending review</span><b>{PendingFindings}</b></div>");
            html.AppendLine("</div>");
            html.AppendLine($"<p>Audit analysis also recorded {gst} GST-related finding(s), {tds} TDS-related finding(s), and {duplicates} duplicate-detection finding(s). {reviewed} finding(s) are marked reviewed or resolved in the audit workspace.</p>");
            html.AppendLine("<p class='note'><b>Important:</b> This generated document is a working-paper template based on information available in Tally Audit Assistant. It is not a legal conclusion, statutory certification, or substitute for professional auditor review. Management should verify every representation before signing.</p>");
            html.AppendLine("<div class='signature'><b>For and on behalf of management</b><br><br><span class='line'></span><br>Name / Designation<br><br>Date: ____________________</div>");
            html.AppendLine("<div class='signature'><b>Acknowledged by auditor</b><br><br><span class='line'></span><br>Name / Firm<br><br>Date: ____________________</div>");
            html.AppendLine($"<div class='footer'>Generated by Tally Audit Assistant on {DateTime.Now:dd-MMM-yyyy HH:mm}. TallyPrime integration is read-only; this application does not modify TallyPrime data.</div>");
            html.AppendLine("</body></html>");

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var dir = string.IsNullOrWhiteSpace(docs) ? Path.Combine(Path.GetTempPath(), "TallyAuditAssistant", "ManagementLetters") : Path.Combine(docs, "TallyAuditManagementLetters");
            Directory.CreateDirectory(dir);
            var safe = Sanitize(company.TallyCompanyName);
            var file = Path.Combine(dir, $"Management_Representation_Letter_{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            await File.WriteAllTextAsync(file, html.ToString(), Encoding.UTF8);

            LastGeneratedFilePath = file;
            StatusMessage = $"Management Representation Letter generated: {file}";
            IsSuccess = true;

            _ = _auditTrailService?.RecordActivityAsync(
                actionType: "Management representation letter generated",
                module: "AUDIT DOCUMENTATION",
                description: $"Generated management representation letter for {company.TallyCompanyName}.",
                companyName: company.TallyCompanyName,
                ct: CancellationToken.None);

            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = file, UseShellExecute = true }); } catch { }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Generation failed: {ex.Message}";
            IsError = true;
        }
        finally { IsLoading = false; }
    }

    private static string ResolveFinancialYear(DateTime date) =>
        date.Month >= 4 ? $"{date.Year}-{(date.Year + 1) % 100:D2}" : $"{date.Year - 1}-{date.Year % 100:D2}";

    private static string H(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray()).Trim('_');
        return string.IsNullOrWhiteSpace(result) ? "Company" : result;
    }
}
