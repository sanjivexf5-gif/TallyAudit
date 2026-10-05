using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class ReportsViewModel : ObservableObject, INavigationAware
{
    private readonly IAuditRepository _repository;
    private readonly ISettingsService _settingsService;
    private readonly IActiveCompanyContext _companyContext;
    private readonly INavigationService _navigationService;
    private readonly IAuditTrailService? _auditTrailService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _activeCompanyName = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private string _lastExportedFilePath = string.Empty;

    [ObservableProperty]
    private bool _hasExportedFile;

    [ObservableProperty]
    private int _totalVouchersCount;

    [ObservableProperty]
    private int _totalExceptionsCount;

    [ObservableProperty]
    private int _gstExceptionsCount;

    [ObservableProperty]
    private int _tdsExceptionsCount;

    [ObservableProperty]
    private int _duplicateExceptionsCount;

    [ObservableProperty]
    private bool _hasData;

    public ReportsViewModel(
        IAuditRepository repository,
        ISettingsService settingsService,
        IActiveCompanyContext companyContext,
        INavigationService navigationService,
        IAuditTrailService? auditTrailService = null)
    {
        _repository = repository;
        _settingsService = settingsService;
        _companyContext = companyContext;
        _navigationService = navigationService;
        _auditTrailService = auditTrailService;

        _companyContext.ActiveCompanyChanged += OnActiveCompanyChanged;
        _ = LoadReportsInfoAsync();
    }

    public async Task OnNavigatedToAsync()
    {
        await LoadReportsInfoAsync();
    }

    private void OnActiveCompanyChanged(object? sender, Company? comp)
    {
        _ = LoadReportsInfoAsync();
    }

    public async Task LoadReportsInfoAsync()
    {
        try
        {
            var comp = await _companyContext.GetActiveCompanyAsync();
            if (comp == null)
            {
                ActiveCompanyName = "No Company Selected";
                HasData = false;
                TotalVouchersCount = 0;
                TotalExceptionsCount = 0;
                GstExceptionsCount = 0;
                TdsExceptionsCount = 0;
                DuplicateExceptionsCount = 0;
                return;
            }

            var current = comp;
            ActiveCompanyName = current.TallyCompanyName;

            TotalVouchersCount = await _repository.GetVoucherCountAsync(current.Id);
            var exceptions = await _repository.GetExceptionsAsync(current.Id, take: 5000);
            TotalExceptionsCount = exceptions.Count;
            GstExceptionsCount = exceptions.Count(e => e.Category == RuleCategory.GST);
            TdsExceptionsCount = exceptions.Count(e => e.Category == RuleCategory.TDS);
            DuplicateExceptionsCount = exceptions.Count(e => e.Category == RuleCategory.DuplicateDetection);

            HasData = TotalVouchersCount > 0 || TotalExceptionsCount > 0;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to load audit metrics: {ex.Message}";
            IsError = true;
        }
    }

    public static string GetSafeExportDirectory()
    {
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(docs))
            {
                var dir = Path.Combine(docs, "TallyAuditReports");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
        catch { }

        try
        {
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(localApp, "TallyAuditAssistant", "Reports");
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch { }

        var tempDir = Path.Combine(Path.GetTempPath(), "TallyAuditReports");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            sb.Append(invalid.Contains(c) || c == ' ' ? '_' : c);
        }
        var res = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(res) ? "Company" : res;
    }

    [RelayCommand]
    public async Task GenerateExcelReportAsync()
    {
        IsLoading = true;
        StatusMessage = "Compiling Microsoft Excel working papers...";
        IsSuccess = false;
        IsError = false;

        try
        {
            var comp = await _companyContext.GetActiveCompanyAsync();
            if (comp == null)
            {
                StatusMessage = "No synchronized company found. Synchronize data from TallyPrime first.";
                IsError = true;
                return;
            }

            var current = comp;

            var allExceptions = await _repository.GetExceptionsAsync(current.Id, take: 5000);
            var totalVouchers = await _repository.GetVoucherCountAsync(current.Id);

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

            void AppendWorksheet(string name, string[] headers, System.Collections.Generic.List<AuditException> items)
            {
                sb.AppendLine($" <Worksheet ss:Name=\"{name}\">");
                sb.AppendLine("  <Table>");
                sb.AppendLine("   <Row ss:Height=\"24\" ss:StyleID=\"Header\">");
                foreach (var h in headers)
                {
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{h}</Data></Cell>");
                }
                sb.AppendLine("   </Row>");

                foreach (var ex in items)
                {
                    var vDate = ex.VoucherDate.HasValue ? ex.VoucherDate.Value.ToString("dd-MMM-yyyy") : "—";
                    var vNo = !string.IsNullOrEmpty(ex.VoucherNumber) ? ex.VoucherNumber : "—";
                    var lName = !string.IsNullOrEmpty(ex.LedgerName) ? ex.LedgerName : "—";
                    var amt = ex.FlaggedAmount.HasValue ? ex.FlaggedAmount.Value : 0m;
                    var note = !string.IsNullOrEmpty(ex.AuditorNote) ? ex.AuditorNote : "—";

                    sb.AppendLine("   <Row ss:Height=\"18\">");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{EscapeXml(ex.RuleName)}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{ex.Category}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{ex.Severity}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{vDate}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{EscapeXml(vNo)}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{EscapeXml(lName)}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"Number\">{amt}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{EscapeXml(ex.SuggestedCorrection ?? "—")}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{ex.Status}</Data></Cell>");
                    sb.AppendLine($"    <Cell><Data ss:Type=\"String\">{EscapeXml(note)}</Data></Cell>");
                    sb.AppendLine("   </Row>");
                }

                sb.AppendLine("  </Table>");
                sb.AppendLine(" </Worksheet>");
            }

            // Summary Worksheet
            sb.AppendLine(" <Worksheet ss:Name=\"Executive Summary\">");
            sb.AppendLine("  <Table>");
            sb.AppendLine("   <Row ss:Height=\"30\" ss:StyleID=\"Title\"><Cell><Data ss:Type=\"String\">TALLY AUDIT ASSISTANT — AUTOMATED AUDIT REPORT</Data></Cell></Row>");
            sb.AppendLine("   <Row><Cell><Data ss:Type=\"String\"></Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">Company Name:</Data></Cell><Cell><Data ss:Type=\"String\">{EscapeXml(current.TallyCompanyName)}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">GSTIN / PAN:</Data></Cell><Cell><Data ss:Type=\"String\">{current.GSTIN} / {current.PAN}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">Report Generated At:</Data></Cell><Cell><Data ss:Type=\"String\">{DateTime.Now:dd-MMM-yyyy HH:mm:ss}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">Total Vouchers Analysed:</Data></Cell><Cell><Data ss:Type=\"Number\">{totalVouchers}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">Total Audit Exceptions:</Data></Cell><Cell><Data ss:Type=\"Number\">{allExceptions.Count}</Data></Cell></Row>");
            sb.AppendLine("  </Table>");
            sb.AppendLine(" </Worksheet>");

            var headers = new[] { "Rule Name", "Category", "Severity", "Voucher Date", "Voucher No", "Ledger Name", "Amount (₹)", "Description / Finding", "Review Status", "Auditor Note" };
            AppendWorksheet("All Exceptions", headers, allExceptions.ToList());
            AppendWorksheet("GST Exceptions", headers, allExceptions.Where(x => x.Category == RuleCategory.GST).ToList());
            AppendWorksheet("TDS Exceptions", headers, allExceptions.Where(x => x.Category == RuleCategory.TDS).ToList());
            AppendWorksheet("Accounting Hygiene", headers, allExceptions.Where(x => x.Category == RuleCategory.GeneralAccounting || x.Category == RuleCategory.DuplicateDetection).ToList());

            sb.AppendLine("</Workbook>");

            var dir = GetSafeExportDirectory();
            var fileName = $"Audit_Working_Papers_{SanitizeFileName(current.TallyCompanyName)}_{DateTime.Now:yyyyMMdd_HHmmss}.xls";
            var filePath = Path.Combine(dir, fileName);

            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);

            LastExportedFilePath = filePath;
            HasExportedFile = true;
            StatusMessage = $"Excel working papers exported successfully to: {filePath}";
            IsSuccess = true;

            if (_auditTrailService != null)
            {
                _ = _auditTrailService.RecordActivityAsync(
                    actionType: "Report exported",
                    module: "REPORTS",
                    description: $"Excel working papers exported to '{fileName}'. Included {allExceptions.Count} exception(s).",
                    companyName: current.TallyCompanyName,
                    ct: CancellationToken.None);
            }

            TryOpenFile(filePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Excel Export failed: {ex.Message}";
            IsError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task GeneratePdfReportAsync()
    {
        IsLoading = true;
        StatusMessage = "Compiling executive audit report...";
        IsSuccess = false;
        IsError = false;

        try
        {
            var comp = await _companyContext.GetActiveCompanyAsync();
            if (comp == null)
            {
                StatusMessage = "No synchronized company found. Synchronize data from TallyPrime first.";
                IsError = true;
                return;
            }

            var current = comp;

            var allExceptions = await _repository.GetExceptionsAsync(current.Id, take: 5000);
            var totalVouchers = await _repository.GetVoucherCountAsync(current.Id);

            var criticalCount = allExceptions.Count(e => e.Severity == SeverityLevel.Critical);
            var highCount = allExceptions.Count(e => e.Severity == SeverityLevel.High);
            var mediumCount = allExceptions.Count(e => e.Severity == SeverityLevel.Medium);
            var lowCount = allExceptions.Count(e => e.Severity == SeverityLevel.Low);
            var pendingCount = allExceptions.Count(e => e.Status == ReviewStatus.Pending);
            var reviewedCount = allExceptions.Count(e => e.Status == ReviewStatus.Reviewed || e.Status == ReviewStatus.Resolved);
            var riskScore = Math.Min(100, criticalCount * 30 + highCount * 15 + mediumCount * 7 + lowCount * 3);
            var riskLabel = riskScore >= 75 ? "CRITICAL" : riskScore >= 50 ? "HIGH" : riskScore >= 25 ? "MEDIUM" : "LOW";

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine(" <meta charset='utf-8'>");
            html.AppendLine(" <title>Tally Audit Assistant — Executive Report</title>");
            html.AppendLine(" <style>");
            html.AppendLine("   body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #0F172A; color: #F8FAFC; margin: 40px; }");
            html.AppendLine("   .card { background: #1E293B; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.3); padding: 24px; border: 1px solid #334155; margin-bottom: 24px; }");
            html.AppendLine("   h1 { color: #38BDF8; font-size: 24px; border-bottom: 2px solid #334155; padding-bottom: 12px; margin-bottom: 16px; }");
            html.AppendLine("   h2 { color: #F8FAFC; font-size: 18px; margin-top: 16px; margin-bottom: 12px; }");
            html.AppendLine("   .kpi-row { display: flex; gap: 16px; margin-top: 16px; }");
            html.AppendLine("   .kpi { background: #0F172A; border: 1px solid #334155; padding: 14px; border-radius: 6px; flex: 1; }");
            html.AppendLine("   .kpi-title { font-size: 11px; color: #94A3B8; text-transform: uppercase; font-weight: 600; }");
            html.AppendLine("   .kpi-val { font-size: 22px; font-weight: 700; color: #38BDF8; margin-top: 4px; }\n   .risk { border: 1px solid #F59E0B; background: #422006; }\n   .risk-score { font-size: 32px; font-weight: 800; color: #FBBF24; }\n   .note { color: #CBD5E1; font-size: 11px; line-height: 1.5; }");
            html.AppendLine("   table { width: 100%; border-collapse: collapse; margin-top: 16px; }");
            html.AppendLine("   th { background: #0F766E; color: white; text-align: left; padding: 10px; font-size: 12px; }");
            html.AppendLine("   td { padding: 10px; border-bottom: 1px solid #334155; font-size: 12px; }");
            html.AppendLine("   tr:nth-child(even) { background-color: #162032; }");
            html.AppendLine(" </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");

            var nowStr = DateTime.Now.ToString("dd-MMM-yyyy HH:mm");
            html.AppendLine(" <div class='card'>");
            html.AppendLine("   <h1>TALLY AUDIT ASSISTANT — STATUTORY AUDIT SUMMARY</h1>");
            html.AppendLine($"   <h2>Target Entity: {System.Net.WebUtility.HtmlEncode(current.TallyCompanyName)}</h2>");
            html.AppendLine($"   <div style='color: #94A3B8;'>GSTIN: {current.GSTIN} | PAN: {current.PAN} | Generated: {nowStr}</div>");
            html.AppendLine("   <div class='kpi-row'>");
            html.AppendLine($"     <div class='kpi'><div class='kpi-title'>Total Transactions Analysed</div><div class='kpi-val'>{totalVouchers:N0}</div></div>");
            html.AppendLine($"     <div class='kpi'><div class='kpi-title'>Total Audit Findings</div><div class='kpi-val' style='color:#F59E0B'>{allExceptions.Count}</div></div>");
            html.AppendLine($"     <div class='kpi'><div class='kpi-title'>GST Discrepancies</div><div class='kpi-val'>{allExceptions.Count(e => e.Category == RuleCategory.GST)}</div></div>");
            html.AppendLine($"     <div class='kpi'><div class='kpi-title'>TDS Variances</div><div class='kpi-val'>{allExceptions.Count(e => e.Category == RuleCategory.TDS)}</div></div>");
            html.AppendLine("   </div>");
            html.AppendLine(" </div>");
            html.AppendLine(" <div class='card risk'>");
            html.AppendLine($"   <h2>Audit Risk Assessment: {riskLabel}</h2>");
            html.AppendLine($"   <div class='risk-score'>{riskScore}/100</div>");
            html.AppendLine($"   <div class='note'>Weighted assessment based on recorded findings: {criticalCount} critical, {highCount} high, {mediumCount} medium and {lowCount} low. This is an audit-prioritisation indicator, not a conclusion of fraud or non-compliance.</div>");
            html.AppendLine("   <div class='kpi-row'>");
            html.AppendLine($"     <div class='kpi'><div class='kpi-title'>Pending Review</div><div class='kpi-val'>{pendingCount}</div></div>");
            html.AppendLine($"     <div class='kpi'><div class='kpi-title'>Reviewed / Resolved</div><div class='kpi-val'>{reviewedCount}</div></div>");
            html.AppendLine($"     <div class='kpi'><div class='kpi-title'>Duplicate Findings</div><div class='kpi-val'>{allExceptions.Count(e => e.Category == RuleCategory.DuplicateDetection)}</div></div>");
            html.AppendLine("   </div>");
            html.AppendLine(" </div>");

            html.AppendLine(" <div class='card'>");
            html.AppendLine("   <h2>Detailed Findings Register</h2>");
            html.AppendLine("   <div class='note'>Source: synchronized Tally data analysed locally by Tally Audit Assistant. The application does not modify Tally data.</div>");
            html.AppendLine("   <table>");
            html.AppendLine("     <tr><th>Rule Name</th><th>Category</th><th>Severity</th><th>Voucher No</th><th>Ledger Name</th><th>Flagged Amount</th><th>Status</th></tr>");
            foreach (var ex in allExceptions)
            {
                var vNo = System.Net.WebUtility.HtmlEncode(ex.VoucherNumber ?? "—");
                var lName = System.Net.WebUtility.HtmlEncode(ex.LedgerName ?? "—");
                var amt = ex.FlaggedAmount?.ToString("C") ?? "—";
                html.AppendLine("     <tr>");
                html.AppendLine($"       <td><strong>{System.Net.WebUtility.HtmlEncode(ex.RuleName)}</strong></td>");
                html.AppendLine($"       <td>{ex.Category}</td>");
                html.AppendLine($"       <td>{ex.Severity}</td>");
                html.AppendLine($"       <td>{vNo}</td>");
                html.AppendLine($"       <td>{lName}</td>");
                html.AppendLine($"       <td style='color:#38BDF8; font-weight:600;'>{amt}</td>");
                html.AppendLine($"       <td>{ex.Status}</td>");
                html.AppendLine("     </tr>");
            }
            html.AppendLine("   </table>");
            html.AppendLine(" </div>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            var dir = GetSafeExportDirectory();
            var fileName = $"Tally_Audit_Report_{SanitizeFileName(current.TallyCompanyName)}_{DateTime.Now:yyyyMMdd_HHmmss}.html";
            var filePath = Path.Combine(dir, fileName);

            await File.WriteAllTextAsync(filePath, html.ToString(), Encoding.UTF8);

            LastExportedFilePath = filePath;
            HasExportedFile = true;
            StatusMessage = $"Automated audit report exported successfully to: {filePath}";
            IsSuccess = true;

            if (_auditTrailService != null)
            {
                _ = _auditTrailService.RecordActivityAsync(
                    actionType: "Report exported",
                    module: "REPORTS",
                    description: $"Automated audit report exported to '{fileName}'. Risk score {riskScore}/100 ({riskLabel}).",
                    companyName: current.TallyCompanyName,
                    ct: CancellationToken.None);
            }

            TryOpenFile(filePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Summary report failed: {ex.Message}";
            IsError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task GenerateCsvReportAsync()
    {
        IsLoading = true;
        StatusMessage = "Compiling standard CSV audit register...";
        IsSuccess = false;
        IsError = false;

        try
        {
            var comp = await _companyContext.GetActiveCompanyAsync();
            if (comp == null)
            {
                StatusMessage = "No synchronized company found. Synchronize data from TallyPrime first.";
                IsError = true;
                return;
            }

            var current = comp;

            var allExceptions = await _repository.GetExceptionsAsync(current.Id, take: 5000);

            var csv = new StringBuilder();
            csv.AppendLine("RuleId,RuleName,Category,Severity,VoucherDate,VoucherNumber,LedgerName,Amount,Description,Status,AuditorNote");

            foreach (var ex in allExceptions)
            {
                var vDate = ex.VoucherDate.HasValue ? ex.VoucherDate.Value.ToString("yyyy-MM-dd") : "";
                var vNo = EscapeCsv(ex.VoucherNumber ?? "");
                var rId = EscapeCsv(ex.RuleId ?? "");
                var rName = EscapeCsv(ex.RuleName);
                var lName = EscapeCsv(ex.LedgerName ?? "");
                var desc = EscapeCsv(ex.SuggestedCorrection ?? "");
                var note = EscapeCsv(ex.AuditorNote ?? "");
                var amt = ex.FlaggedAmount.HasValue ? ex.FlaggedAmount.Value.ToString("F2") : "0.00";

                csv.AppendLine($"{rId},{rName},{ex.Category},{ex.Severity},{vDate},{vNo},{lName},{amt},{desc},{ex.Status},{note}");
            }

            var dir = GetSafeExportDirectory();
            var fileName = $"Audit_Exceptions_{SanitizeFileName(current.TallyCompanyName)}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var filePath = Path.Combine(dir, fileName);

            await File.WriteAllTextAsync(filePath, csv.ToString(), Encoding.UTF8);

            LastExportedFilePath = filePath;
            HasExportedFile = true;
            StatusMessage = $"CSV Audit register exported successfully to: {filePath}";
            IsSuccess = true;

            if (_auditTrailService != null)
            {
                _ = _auditTrailService.RecordActivityAsync(
                    actionType: "Report exported",
                    module: "REPORTS",
                    description: $"CSV audit findings register exported to '{fileName}'. Included {allExceptions.Count} exception(s).",
                    companyName: current.TallyCompanyName,
                    ct: CancellationToken.None);
            }

            TryOpenFile(filePath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"CSV Export failed: {ex.Message}";
            IsError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void OpenFile()
    {
        if (!string.IsNullOrEmpty(LastExportedFilePath) && File.Exists(LastExportedFilePath))
        {
            TryOpenFile(LastExportedFilePath);
        }
    }

    [RelayCommand]
    public void OpenFolder()
    {
        try
        {
            var dir = !string.IsNullOrEmpty(LastExportedFilePath) && File.Exists(LastExportedFilePath)
                ? Path.GetDirectoryName(LastExportedFilePath)
                : GetSafeExportDirectory();

            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to open export folder: {ex.Message}";
        }
    }

    [RelayCommand]
    public void GoToAudit()
    {
        _navigationService.Navigate("Dashboard");
    }

    [RelayCommand]
    public void GoToSync()
    {
        _navigationService.Navigate("Sync");
    }

    private static void TryOpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // Do not fail if no associated viewer is installed
        }
    }

    private static string EscapeXml(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
    }

    private static string EscapeCsv(string text)
    {
        if (string.IsNullOrEmpty(text)) return "\"\"";
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
