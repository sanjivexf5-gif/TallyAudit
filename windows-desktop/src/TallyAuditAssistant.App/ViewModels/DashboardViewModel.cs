using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IAuditRepository _repository;
    private readonly ITallyConnection _tallyConnection;
    private readonly IAuditEngine _auditEngine;
    private readonly ISettingsService _settingsService;
    private readonly ITallyDrillDownService _drillDownService;
    private readonly IAuditAssistantService _aiService;
    private readonly ILogger<DashboardViewModel> _logger;

    [ObservableProperty]
    private string _aiExplanationText = string.Empty;

    [ObservableProperty]
    private string _aiReviewQuestionsText = string.Empty;

    [ObservableProperty]
    private string _aiDraftRemarkText = string.Empty;

    [ObservableProperty]
    private string _aiAuditRunSummaryText = "AI Audit Summary is not generated yet. Configure a Gemini key and run an audit or click generate.";

    [ObservableProperty]
    private bool _isAiLoading = false;

    [ObservableProperty]
    private string _aiStatusMessage = string.Empty;

    [ObservableProperty]
    private string _aiChatQuery = string.Empty;

    [ObservableProperty]
    private string _aiChatResponse = string.Empty;

    [ObservableProperty]
    private bool _isAiChatLoading = false;

    [ObservableProperty]
    private string _activeCompanyName = "Apex Industrial Solutions Pvt Ltd";

    [ObservableProperty]
    private string _financialYear = "FY 2025-26";

    // Category Counts
    [ObservableProperty]
    private int _gstExceptionsCount = 0;

    [ObservableProperty]
    private int _tdsExceptionsCount = 0;

    [ObservableProperty]
    private int _accountingExceptionsCount = 0;

    [ObservableProperty]
    private int _duplicateExceptionsCount = 0;

    [ObservableProperty]
    private int _ledgerExceptionsCount = 0;

    [ObservableProperty]
    private int _voucherSequencingCount = 0;

    [ObservableProperty]
    private int _anomalyDetectionCount = 0;

    [ObservableProperty]
    private int _bankingCount = 0;

    [ObservableProperty]
    private int _reconciliationTotalChecks = 12;

    [ObservableProperty]
    private int _reconciliationPassed = 12;

    [ObservableProperty]
    private int _reconciliationDifferences = 0;

    [ObservableProperty]
    private int _reconciliationWarnings = 0;

    [ObservableProperty]
    private int _reconciliationUnableToCheck = 0;

    [ObservableProperty]
    private int _totalFindings = 0;

    // Status Counts
    [ObservableProperty]
    private int _unreviewedExceptionsCount = 0;

    [ObservableProperty]
    private int _reviewedExceptionsCount = 0;

    [ObservableProperty]
    private int _acceptedExceptionsCount = 0;

    [ObservableProperty]
    private int _needsFollowUpExceptionsCount = 0;

    [ObservableProperty]
    private int _highPriorityCount = 0;

    [ObservableProperty]
    private int _pendingAuditorReviews = 0;

    [ObservableProperty]
    private int _auditCompletionPercentage = 0;

    [ObservableProperty]
    private int _totalVouchersSynchronized = 0;

    [ObservableProperty]
    private string _lastSyncTime = "Never";

    [ObservableProperty]
    private string _lastAuditRunTime = "Never";

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private bool _isAuditing = false;

    [ObservableProperty]
    private string _auditStatusText = "Ready to start audit";

    [ObservableProperty]
    private AuditException? _selectedException;

    [ObservableProperty]
    private TallyVoucherDrillDownInfo? _selectedVoucherDrillDown;

    // Filtering, Search and Sorting Properties
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedCategoryFilter = "All";

    [ObservableProperty]
    private string _selectedSeverityFilter = "All";

    [ObservableProperty]
    private string _selectedStatusFilter = "All";

    [ObservableProperty]
    private string _selectedSortColumn = "Date";

    [ObservableProperty]
    private bool _isSortDescending = true;

    [ObservableProperty]
    private string _auditorNoteInput = string.Empty;

    public ObservableCollection<AuditException> RecentExceptions { get; } = new();
    public ObservableCollection<AuditRun> AuditRuns { get; } = new();

    public DashboardViewModel(
        IAuditRepository repository, 
        ITallyConnection tallyConnection,
        IAuditEngine auditEngine,
        ISettingsService settingsService,
        ITallyDrillDownService drillDownService,
        IAuditAssistantService aiService,
        ILogger<DashboardViewModel> logger)
    {
        _repository = repository;
        _tallyConnection = tallyConnection;
        _auditEngine = auditEngine;
        _settingsService = settingsService;
        _drillDownService = drillDownService;
        _aiService = aiService;
        _logger = logger;
        _ = LoadDashboardDataAsync();
    }

    [RelayCommand]
    private async Task RefreshDashboardAsync()
    {
        await LoadDashboardDataAsync();
    }

    [RelayCommand]
    private async Task RunCompleteAuditAsync()
    {
        IsAuditing = true;
        AuditStatusText = "Preparing audit environment...";

        try
        {
            var activeName = await _settingsService.GetSettingAsync("ActiveCompany", "Apex Industrial Solutions Pvt Ltd");
            var companies = await _repository.GetAllCompaniesAsync();
            if (companies == null || companies.Count == 0)
            {
                AuditStatusText = "No company data synchronized yet. Please connect to Tally and synchronize first.";
                return;
            }
            var current = companies.FirstOrDefault(c => c.TallyCompanyName == activeName) ?? companies[0];

            var fromDate = DateTime.Today.AddYears(-1);
            var toDate = DateTime.Today;

            var fromDateStr = await _settingsService.GetSettingAsync("AuditPeriodFrom", "");
            var toDateStr = await _settingsService.GetSettingAsync("AuditPeriodTo", "");

            if (DateTime.TryParseExact(fromDateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var fD))
            {
                fromDate = fD;
            }
            if (DateTime.TryParseExact(toDateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var tD))
            {
                toDate = tD;
            }

            // Create Run History Record
            var run = new AuditRun
            {
                CompanyId = current.Id,
                Period = $"{fromDate:dd-MMM-yyyy} to {toDate:dd-MMM-yyyy}",
                StartTime = DateTime.UtcNow,
                TransactionsAnalysed = TotalVouchersSynchronized,
                Status = "Running"
            };
            await _repository.SaveAuditRunAsync(run);

            var context = new AuditExecutionContext(activeName, fromDate, toDate);
            
            AuditStatusText = "Executing 19 automated GST/TDS/Accounting hygiene audit rules...";
            var results = await _auditEngine.ExecuteAuditAsync(context);

            // Update Run Record
            run.EndTime = DateTime.UtcNow;
            run.FindingsGenerated = results.Count;
            run.Status = "Completed";
            await _repository.SaveAuditRunAsync(run);

            AuditStatusText = $"Audit run complete! Discovered {results.Count} potential exceptions.";
            await LoadDashboardDataAsync();
        }
        catch (Exception ex)
        {
            AuditStatusText = $"Audit execution failed: {ex.Message}";
        }
        finally
        {
            IsAuditing = false;
        }
    }

    [RelayCommand]
    private async Task SaveExceptionStatusAsync()
    {
        if (SelectedException == null) return;

        try
        {
            await _repository.UpdateExceptionStatusAsync(SelectedException.Id, SelectedException.Status, AuditorNoteInput);
            AuditStatusText = "Review status and remarks saved.";
            await LoadDashboardDataAsync();
        }
        catch (Exception ex)
        {
            AuditStatusText = $"Failed to save status: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task MarkAsReviewedAsync()
    {
        if (SelectedException == null) return;
        SelectedException.Status = ReviewStatus.Reviewed;
        await SaveExceptionStatusAsync();
    }

    [RelayCommand]
    private async Task MarkAsAcceptedAsync()
    {
        if (SelectedException == null) return;
        SelectedException.Status = ReviewStatus.Resolved;
        await SaveExceptionStatusAsync();
    }

    [RelayCommand]
    private async Task MarkAsNeedsFollowUpAsync()
    {
        if (SelectedException == null) return;
        SelectedException.Status = ReviewStatus.RequiresClientClarification;
        await SaveExceptionStatusAsync();
    }

    // Property triggers for instant filtering & sorting without hitting manually triggered filters
    async partial void OnSearchQueryChanged(string value) => await LoadDashboardDataAsync();
    async partial void OnSelectedCategoryFilterChanged(string value) => await LoadDashboardDataAsync();
    async partial void OnSelectedSeverityFilterChanged(string value) => await LoadDashboardDataAsync();
    async partial void OnSelectedStatusFilterChanged(string value) => await LoadDashboardDataAsync();
    async partial void OnSelectedSortColumnChanged(string value) => await LoadDashboardDataAsync();
    async partial void OnIsSortDescendingChanged(bool value) => await LoadDashboardDataAsync();

    [RelayCommand]
    private async Task SetCategoryFilterAsync(string category)
    {
        SelectedCategoryFilter = category;
    }

    [RelayCommand]
    private async Task SetStatusFilterAsync(string status)
    {
        SelectedStatusFilter = status;
    }

    [RelayCommand]
    private async Task GenerateExcelReportAsync()
    {
        try
        {
            var activeName = await _settingsService.GetSettingAsync("ActiveCompany", "Apex Industrial Solutions Pvt Ltd");
            var companies = await _repository.GetAllCompaniesAsync();
            if (companies.Count == 0) return;
            var current = companies.FirstOrDefault(c => c.TallyCompanyName == activeName) ?? companies[0];
            
            var allExceptions = await _repository.GetExceptionsAsync(current.Id, take: 1000);
            
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
            sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
            sb.AppendLine(" xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
            sb.AppendLine(" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"");
            sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\"");
            sb.AppendLine(" xmlns:html=\"http://www.w3.org/TR/REC-html40\">");
            
            sb.AppendLine(" <Styles>");
            sb.AppendLine("  <Style ss:ID=\"Default\" ss:Name=\"Normal\">");
            sb.AppendLine("   <Alignment ss:Vertical=\"Bottom\"/>");
            sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" x:CharSet=\"1\" x:Family=\"Swiss\" ss:Size=\"11\" ss:Color=\"#000000\"/>");
            sb.AppendLine("  </Style>");
            sb.AppendLine("  <Style ss:ID=\"Header\">");
            sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" x:CharSet=\"1\" x:Family=\"Swiss\" ss:Size=\"12\" ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/>");
            sb.AppendLine("   <Interior ss:Color=\"#4F46E5\" ss:Pattern=\"Solid\"/>");
            sb.AppendLine("  </Style>");
            sb.AppendLine("  <Style ss:ID=\"Title\">");
            sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" x:CharSet=\"1\" x:Family=\"Swiss\" ss:Size=\"16\" ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/>");
            sb.AppendLine("   <Interior ss:Color=\"#1E1B4B\" ss:Pattern=\"Solid\"/>");
            sb.AppendLine("  </Style>");
            sb.AppendLine("  <Style ss:ID=\"BoldText\">");
            sb.AppendLine("   <Font ss:FontName=\"Segoe UI\" x:CharSet=\"1\" x:Family=\"Swiss\" ss:Size=\"11\" ss:Bold=\"1\"/>");
            sb.AppendLine("  </Style>");
            sb.AppendLine(" </Styles>");

            void AppendWorksheet(string name, string[] headers, System.Collections.Generic.List<AuditException> exceptions)
            {
                sb.AppendLine($" <Worksheet ss:Name=\"{name}\">");
                sb.AppendLine("  <Table>");
                
                sb.AppendLine("   <Row ss:Height=\"24\" ss:StyleID=\"Header\">");
                foreach (var h in headers)
                {
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{h}</Data></Cell>");
                }
                sb.AppendLine("   </Row>");
                
                foreach (var ex in exceptions)
                {
                    var vDate = ex.VoucherDate.HasValue ? ex.VoucherDate.Value.ToString("dd-MMM-yyyy") : "—";
                    var vNo = !string.IsNullOrEmpty(ex.VoucherNumber) ? ex.VoucherNumber : "—";
                    var lName = !string.IsNullOrEmpty(ex.LedgerName) ? ex.LedgerName : "—";
                    var amt = ex.FlaggedAmount.HasValue ? ex.FlaggedAmount.Value : 0m;
                    var note = !string.IsNullOrEmpty(ex.AuditorNote) ? ex.AuditorNote : "—";

                    sb.AppendLine("   <Row ss:Height=\"18\">");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{ex.RuleName}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{ex.Category}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{ex.Severity}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{vDate}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{vNo}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{lName}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"Number\">{amt}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{ex.Status}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{note}</Data></Cell>");
                    sb.AppendLine("   </Row>");
                }
                
                sb.AppendLine("  </Table>");
                sb.AppendLine(" </Worksheet>");
            }

            var gstinStr = !string.IsNullOrEmpty(current.GSTIN) ? current.GSTIN : "—";
            var panStr = !string.IsNullOrEmpty(current.PAN) ? current.PAN : "—";

            sb.AppendLine(" <Worksheet ss:Name=\"Audit Summary\">");
            sb.AppendLine("  <Table ss:ExpandedColumnCount=\"2\">");
            sb.AppendLine("   <Column ss:Width=\"180\"/>");
            sb.AppendLine("   <Column ss:Width=\"240\"/>");
            sb.AppendLine("   <Row ss:Height=\"30\" ss:StyleID=\"Title\"><Cell ss:MergeAcross=\"1\"><Data ss:Type=\"String\">Tally Audit Assistant - Summary</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">Company Name</Data></Cell><Cell><Data ss:Type=\"String\">{current.TallyCompanyName}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">GSTIN</Data></Cell><Cell><Data ss:Type=\"String\">{gstinStr}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">PAN</Data></Cell><Cell><Data ss:Type=\"String\">{panStr}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">Audit Period</Data></Cell><Cell><Data ss:Type=\"String\">{FinancialYear}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">Total Trans. Analysed</Data></Cell><Cell><Data ss:Type=\"Number\">{TotalVouchersSynchronized}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">Total Exceptions</Data></Cell><Cell><Data ss:Type=\"Number\">{allExceptions.Count}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">  - GST Exceptions</Data></Cell><Cell><Data ss:Type=\"Number\">{GstExceptionsCount}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">  - TDS Exceptions</Data></Cell><Cell><Data ss:Type=\"Number\">{TdsExceptionsCount}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">  - Duplicate Candidates</Data></Cell><Cell><Data ss:Type=\"Number\">{DuplicateExceptionsCount}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">  - Ledger Consistency</Data></Cell><Cell><Data ss:Type=\"Number\">{LedgerExceptionsCount}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">  - Reconciliation Checks</Data></Cell><Cell><Data ss:Type=\"Number\">{ReconciliationTotalChecks}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">  - Reconciliation Passed</Data></Cell><Cell><Data ss:Type=\"Number\">{ReconciliationPassed}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">  - Reconciliation Differences</Data></Cell><Cell><Data ss:Type=\"Number\">{ReconciliationDifferences}</Data></Cell></Row>");
            sb.AppendLine("  </Table>");
            sb.AppendLine(" </Worksheet>");

            var colHeaders = new[] { "Rule Name", "Category", "Severity", "Date", "Voucher No", "Ledger/Party", "Amount", "Status", "Remarks" };
            
            AppendWorksheet("All Findings", colHeaders, allExceptions.ToList());
            AppendWorksheet("Reconciliation Differences", colHeaders, allExceptions.Where(x => x.RuleId != null && x.RuleId.StartsWith("REC-")).ToList());
            AppendWorksheet("GST Findings", colHeaders, allExceptions.Where(x => x.Category == RuleCategory.GST).ToList());
            AppendWorksheet("TDS Findings", colHeaders, allExceptions.Where(x => x.Category == RuleCategory.TDS).ToList());
            AppendWorksheet("Duplicate Findings", colHeaders, allExceptions.Where(x => x.Category == RuleCategory.DuplicateDetection).ToList());
            AppendWorksheet("Ledger Findings", colHeaders, allExceptions.Where(x => x.Category == RuleCategory.GeneralAccounting).ToList());
            AppendWorksheet("Review Status", colHeaders, allExceptions.Where(x => x.Status != ReviewStatus.Pending).ToList());

            sb.AppendLine("</Workbook>");
            
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"Audit_Working_Papers_{current.TallyCompanyName.Replace(" ", "_")}.xls");
            await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
            
            AuditStatusText = $"Excel Working Papers exported successfully to {path}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AuditStatusText = $"Excel Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task GeneratePdfReportAsync()
    {
        try
        {
            var activeName = await _settingsService.GetSettingAsync("ActiveCompany", "Apex Industrial Solutions Pvt Ltd");
            var companies = await _repository.GetAllCompaniesAsync();
            if (companies.Count == 0) return;
            var current = companies.FirstOrDefault(c => c.TallyCompanyName == activeName) ?? companies[0];
            
            var allExceptions = await _repository.GetExceptionsAsync(current.Id, take: 1000);
            
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine(" <title>Tally Audit Report</title>");
            html.AppendLine(" <style>");
            html.AppendLine("   body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #F9FAFB; color: #111827; margin: 40px; }");
            html.AppendLine("   .card { background: white; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.05); padding: 24px; border: 1px solid #E5E7EB; margin-bottom: 24px; }");
            html.AppendLine("   h1 { color: #1E3A8A; font-size: 24px; border-bottom: 2px solid #E5E7EB; padding-bottom: 12px; margin-bottom: 16px; }");
            html.AppendLine("   h2 { color: #1E3A8A; font-size: 18px; margin-top: 24px; }");
            html.AppendLine("   .grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; }");
            html.AppendLine("   .metric-box { background: #F3F4F6; padding: 16px; border-radius: 6px; text-align: center; border: 1px solid #E5E7EB; }");
            html.AppendLine("   .metric-title { font-size: 11px; font-weight: bold; color: #4B5563; text-transform: uppercase; }");
            html.AppendLine("   .metric-value { font-size: 24px; font-weight: bold; color: #1E3A8A; margin-top: 4px; }");
            html.AppendLine("   table { width: 100%; border-collapse: collapse; margin-top: 16px; }");
            html.AppendLine("   th { background: #1E3A8A; color: white; text-align: left; padding: 10px; font-size: 12px; }");
            html.AppendLine("   td { padding: 10px; border-bottom: 1px solid #E5E7EB; font-size: 12px; }");
            html.AppendLine("   .tag-high { background: #FEE2E2; color: #991B1B; padding: 2px 6px; border-radius: 4px; font-weight: bold; }");
            html.AppendLine("   .tag-med { background: #FEF3C7; color: #92400E; padding: 2px 6px; border-radius: 4px; font-weight: bold; }");
            html.AppendLine("   .tag-low { background: #D1FAE5; color: #065F46; padding: 2px 6px; border-radius: 4px; font-weight: bold; }");
            html.AppendLine(" </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            
            var nowStr = DateTime.Now.ToString("dd-MMM-yyyy HH:mm");
            html.AppendLine(" <div class='card'>");
            html.AppendLine("   <div style='display: flex; justify-content: space-between;'>");
            html.AppendLine("     <div>");
            html.AppendLine("       <h1 style='border-bottom:none; margin-bottom:4px;'>TALLY AUDIT ASSISTANT</h1>");
            html.AppendLine("       <div style='color: #4B5563; font-size: 14px;'>EXECUTIVE AUDIT SUMMARY SUMMARY SUMMARY SUMMARY SUMMARY</div>");
            html.AppendLine("     </div>");
            html.AppendLine("     <div style='text-align: right; font-size: 12px;'>");
            html.AppendLine($"       <div><strong>Date:</strong> {nowStr}</div>");
            html.AppendLine($"       <div><strong>FY Period:</strong> {FinancialYear}</div>");
            html.AppendLine("     </div>");
            html.AppendLine("   </div>");
            html.AppendLine(" </div>");

            var gstinDisplay = !string.IsNullOrEmpty(current.GSTIN) ? current.GSTIN : "—";
            var panDisplay = !string.IsNullOrEmpty(current.PAN) ? current.PAN : "—";
            html.AppendLine(" <div class='card'>");
            html.AppendLine($"   <h2>Audit Target: {current.TallyCompanyName}</h2>");
            html.AppendLine($"   <div style='color:#4B5563; margin-bottom: 16px;'>GSTIN: {gstinDisplay} &nbsp;|&nbsp; PAN: {panDisplay}</div>");
            
            html.AppendLine("   <div class='grid'>");
            html.AppendLine($"     <div class='metric-box'><div class='metric-title'>Trans. Analysed</div><div class='metric-value'>{TotalVouchersSynchronized}</div></div>");
            html.AppendLine($"     <div class='metric-box'><div class='metric-title'>Total Exceptions</div><div class='metric-value'>{allExceptions.Count}</div></div>");
            html.AppendLine($"     <div class='metric-box'><div class='metric-title'>GST Findings</div><div class='metric-value'>{GstExceptionsCount}</div></div>");
            html.AppendLine($"     <div class='metric-box'><div class='metric-title'>TDS Findings</div><div class='metric-value'>{TdsExceptionsCount}</div></div>");
            html.AppendLine("   </div>");
            html.AppendLine(" </div>");

            html.AppendLine(" <div class='card'>");
            html.AppendLine("   <h2>Detailed Exception Findings Summary Workpapers</h2>");
            html.AppendLine("   <table>");
            html.AppendLine("     <tr><th>Rule Name</th><th>Category</th><th>Severity</th><th>Voucher No</th><th>Date</th><th>Ledger Name</th><th>Amount</th><th>Status</th></tr>");
            
            foreach (var ex in allExceptions)
            {
                var sevClass = ex.Severity >= SeverityLevel.High ? "tag-high" : (ex.Severity == SeverityLevel.Medium ? "tag-med" : "tag-low");
                var vNo = ex.VoucherNumber ?? "—";
                var vDate = ex.VoucherDate?.ToString("dd-MMM-yyyy") ?? "—";
                var lName = ex.LedgerName ?? "—";
                var amt = ex.FlaggedAmount?.ToString("C") ?? "—";
                var correction = ex.SuggestedCorrection ?? string.Empty;

                html.AppendLine("     <tr>");
                html.AppendLine($"       <td><strong>{ex.RuleName}</strong><br><span style='font-size:10px; color:#6B7280;'>{correction}</span></td>");
                html.AppendLine($"       <td>{ex.Category}</td>");
                html.AppendLine($"       <td><span class='{sevClass}'>{ex.Severity}</span></td>");
                html.AppendLine($"       <td>{vNo}</td>");
                html.AppendLine($"       <td>{vDate}</td>");
                html.AppendLine($"       <td>{lName}</td>");
                html.AppendLine($"       <td>{amt}</td>");
                html.AppendLine($"       <td>{ex.Status}</td>");
                html.AppendLine("     </tr>");
            }
            html.AppendLine("   </table>");
            html.AppendLine(" </div>");

            var recExceptions = allExceptions.Where(x => x.RuleId != null && x.RuleId.StartsWith("REC-")).ToList();
            if (recExceptions.Count > 0)
            {
                html.AppendLine(" <div class='card'>");
                html.AppendLine("   <h2>Reconciliation &amp; Cross-Verification Differences</h2>");
                html.AppendLine("   <table>");
                html.AppendLine("     <tr><th>Reconciliation Rule</th><th>Category</th><th>Severity</th><th>Voucher No</th><th>Date</th><th>Ledger Name</th><th>Expected vs Actual / Difference</th><th>Status</th></tr>");
                
                foreach (var ex in recExceptions)
                {
                    var sevClass = ex.Severity >= SeverityLevel.High ? "tag-high" : (ex.Severity == SeverityLevel.Medium ? "tag-med" : "tag-low");
                    var vNo = ex.VoucherNumber ?? "—";
                    var vDate = ex.VoucherDate?.ToString("dd-MMM-yyyy") ?? "—";
                    var lName = ex.LedgerName ?? "—";
                    var amt = ex.FlaggedAmount?.ToString("C") ?? "—";
                    var correction = ex.SuggestedCorrection ?? string.Empty;

                    html.AppendLine("     <tr>");
                    html.AppendLine($"       <td><strong>{ex.RuleName}</strong><br><span style='font-size:10px; color:#6B7280;'>{correction}</span></td>");
                    html.AppendLine($"       <td>{ex.Category}</td>");
                    html.AppendLine($"       <td><span class='{sevClass}'>{ex.Severity}</span></td>");
                    html.AppendLine($"       <td>{vNo}</td>");
                    html.AppendLine($"       <td>{vDate}</td>");
                    html.AppendLine($"       <td>{lName}</td>");
                    html.AppendLine($"       <td><strong style='color:#DC2626;'>{amt}</strong></td>");
                    html.AppendLine($"       <td>{ex.Status}</td>");
                    html.AppendLine("     </tr>");
                }
                html.AppendLine("   </table>");
                html.AppendLine(" </div>");
            }

            html.AppendLine("</body>");
            html.AppendLine("</html>");
            
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"Audit_Executive_Summary_{current.TallyCompanyName.Replace(" ", "_")}.html");
            await File.WriteAllTextAsync(path, html.ToString(), Encoding.UTF8);
            
            AuditStatusText = $"PDF-styled HTML Summary Report saved to {path}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AuditStatusText = $"PDF Summary generation failed: {ex.Message}";
        }
    }

    async partial void OnSelectedExceptionChanged(AuditException? value)
    {
        if (value == null)
        {
            SelectedVoucherDrillDown = null;
            AuditorNoteInput = string.Empty;
            return;
        }

        AuditorNoteInput = value.AuditorNote ?? string.Empty;

        try
        {
            var drillDown = await _drillDownService.GetVoucherDrillDownAsync(value.CompanyId, value.EntityId);
            if (drillDown != null)
            {
                SelectedVoucherDrillDown = drillDown;
            }
            else
            {
                SelectedVoucherDrillDown = new TallyVoucherDrillDownInfo
                {
                    CompanyName = ActiveCompanyName,
                    VoucherNumber = value.VoucherNumber ?? "—",
                    VoucherTypeName = value.EntityType,
                    VoucherDate = value.VoucherDate ?? DateTime.Today,
                    PartyLedgerName = value.LedgerName ?? "—",
                    TotalAmount = value.FlaggedAmount ?? 0,
                    Narration = value.SuggestedCorrection ?? "Details loaded from local SQLite audit database.",
                    Entries = new List<TallyVoucherLinePosting>
                    {
                        new TallyVoucherLinePosting 
                        { 
                            LedgerName = value.LedgerName ?? "—", 
                            Amount = value.FlaggedAmount ?? 0, 
                            IsDebit = true,
                            ParentGroup = value.Category.ToString()
                        }
                    }
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load drill down details for exception {ExceptionId}", value.Id);
        }
    }

    private async Task LoadDashboardDataAsync()
    {
        IsLoading = true;
        try
        {
            var activeName = await _settingsService.GetSettingAsync("ActiveCompany", string.Empty);
            var companies = await _repository.GetAllCompaniesAsync();
            if (companies.Count > 0)
            {
                var current = companies.FirstOrDefault(c => c.TallyCompanyName == activeName) ?? companies[0];
                ActiveCompanyName = current.TallyCompanyName;
                TotalVouchersSynchronized = await _repository.GetVoucherCountAsync(current.Id);
                LastSyncTime = current.LastSyncDate?.ToString("g") ?? "Never";

                // Dynamic SQLite-based parameterized filters
                var exceptions = await _repository.GetExceptionsFilteredAsync(
                    current.Id,
                    SelectedCategoryFilter,
                    SelectedSeverityFilter,
                    SelectedStatusFilter,
                    SearchQuery,
                    SelectedSortColumn,
                    IsSortDescending
                );

                RecentExceptions.Clear();
                foreach (var ex in exceptions)
                {
                    RecentExceptions.Add(ex);
                }

                // Load reconciliation metrics
                var allExceptions = await _repository.GetExceptionsAsync(current.Id, take: 1000);
                var recExceptions = allExceptions.Where(x => x.RuleId != null && x.RuleId.StartsWith("REC-")).ToList();
                ReconciliationTotalChecks = 12;
                ReconciliationDifferences = recExceptions.Count;
                ReconciliationWarnings = recExceptions.Count(x => x.Severity < SeverityLevel.High);
                ReconciliationPassed = Math.Max(0, 12 - recExceptions.Select(x => x.RuleId).Distinct().Count());
                ReconciliationUnableToCheck = 0;

                // General counts
                GstExceptionsCount = await _repository.GetExceptionCountAsync(current.Id, category: RuleCategory.GST);
                TdsExceptionsCount = await _repository.GetExceptionCountAsync(current.Id, category: RuleCategory.TDS);
                DuplicateExceptionsCount = await _repository.GetExceptionCountAsync(current.Id, category: RuleCategory.DuplicateDetection);
                LedgerExceptionsCount = await _repository.GetExceptionCountAsync(current.Id, category: RuleCategory.GeneralAccounting);
                VoucherSequencingCount = await _repository.GetExceptionCountAsync(current.Id, category: RuleCategory.VoucherSequencing);
                AnomalyDetectionCount = await _repository.GetExceptionCountAsync(current.Id, category: RuleCategory.AnomalyDetection);
                BankingCount = await _repository.GetExceptionCountAsync(current.Id, category: RuleCategory.Banking);

                TotalFindings = GstExceptionsCount + TdsExceptionsCount + DuplicateExceptionsCount + LedgerExceptionsCount + VoucherSequencingCount + AnomalyDetectionCount + BankingCount;
                
                AccountingExceptionsCount = LedgerExceptionsCount; // Alias compatibility
                HighPriorityCount = await _repository.GetExceptionCountAsync(current.Id, minSeverity: SeverityLevel.High);
                PendingAuditorReviews = await _repository.GetExceptionCountAsync(current.Id, status: ReviewStatus.Pending);

                // Specific status counts
                UnreviewedExceptionsCount = PendingAuditorReviews;
                ReviewedExceptionsCount = await _repository.GetExceptionCountAsync(current.Id, status: ReviewStatus.Reviewed);
                AcceptedExceptionsCount = await _repository.GetExceptionCountAsync(current.Id, status: ReviewStatus.Resolved);
                NeedsFollowUpExceptionsCount = await _repository.GetExceptionCountAsync(current.Id, status: ReviewStatus.RequiresClientClarification);

                // Compute completion progress
                var totalExceptions = TotalFindings;
                if (totalExceptions > 0)
                {
                    var reviewed = totalExceptions - PendingAuditorReviews;
                    AuditCompletionPercentage = Math.Clamp((int)Math.Round(((double)reviewed / totalExceptions) * 100), 0, 100);
                }
                else
                {
                    AuditCompletionPercentage = 100;
                }

                // Fetch previous runs history list
                var runs = await _repository.GetAuditRunsAsync(current.Id);
                AuditRuns.Clear();
                foreach (var r in runs)
                {
                    AuditRuns.Add(r);
                }

                if (runs.Count > 0)
                {
                    LastAuditRunTime = runs[0].EndTime.ToLocalTime().ToString("g");
                }
                else
                {
                    LastAuditRunTime = "Never";
                }
            }

            var fy = await _settingsService.GetSettingAsync("FinancialYear", "FY 2025-26");
            FinancialYear = fy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dashboard data");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
