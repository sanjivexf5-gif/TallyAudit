using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.ViewModels;

public partial class ReportsViewModel : ObservableObject
{
    private readonly IAuditRepository _repository;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _activeCompanyName = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ReportsViewModel(IAuditRepository repository, ISettingsService settingsService)
    {
        _repository = repository;
        _settingsService = settingsService;
        _ = LoadReportsInfoAsync();
    }

    public async Task LoadReportsInfoAsync()
    {
        var activeName = await _settingsService.GetSettingAsync("ActiveCompany", "Apex Industrial Solutions Pvt Ltd");
        ActiveCompanyName = activeName;
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
            var TotalVouchersSynchronized = await _repository.GetVoucherCountAsync(current.Id);
            
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
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">Total Trans. Analysed</Data></Cell><Cell><Data ss:Type=\"Number\">{TotalVouchersSynchronized}</Data></Cell></Row>");
            sb.AppendLine($"   <Row><Cell ss:StyleID=\"BoldText\"><Data ss:Type=\"String\">Total Exceptions</Data></Cell><Cell><Data ss:Type=\"Number\">{allExceptions.Count}</Data></Cell></Row>");
            sb.AppendLine("  </Table>");
            sb.AppendLine(" </Worksheet>");

            var colHeaders = new[] { "Rule Name", "Category", "Severity", "Date", "Voucher No", "Ledger/Party", "Amount", "Status", "Remarks" };
            
            AppendWorksheet("All Findings", colHeaders, allExceptions.ToList());
            AppendWorksheet("GST Findings", colHeaders, allExceptions.Where(x => x.Category == RuleCategory.GST).ToList());
            AppendWorksheet("TDS Findings", colHeaders, allExceptions.Where(x => x.Category == RuleCategory.TDS).ToList());
            AppendWorksheet("Duplicate Findings", colHeaders, allExceptions.Where(x => x.Category == RuleCategory.DuplicateDetection).ToList());
            AppendWorksheet("Ledger Findings", colHeaders, allExceptions.Where(x => x.Category == RuleCategory.GeneralAccounting).ToList());

            sb.AppendLine("</Workbook>");
            
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"Audit_Working_Papers_{current.TallyCompanyName.Replace(" ", "_")}.xls");
            await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
            
            StatusMessage = $"Excel working papers exported successfully to {path}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Excel Export failed: {ex.Message}";
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
            var TotalVouchersSynchronized = await _repository.GetVoucherCountAsync(current.Id);
            
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
            html.AppendLine("   table { width: 100%; border-collapse: collapse; margin-top: 16px; }");
            html.AppendLine("   th { background: #1E3A8A; color: white; text-align: left; padding: 10px; font-size: 12px; }");
            html.AppendLine("   td { padding: 10px; border-bottom: 1px solid #E5E7EB; font-size: 12px; }");
            html.AppendLine(" </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            
            var nowStr = DateTime.Now.ToString("dd-MMM-yyyy HH:mm");
            html.AppendLine(" <div class='card'>");
            html.AppendLine("   <h1>TALLY AUDIT ASSISTANT</h1>");
            html.AppendLine($"   <h2>Audit Target: {current.TallyCompanyName}</h2>");
            html.AppendLine($"   <div>Generated on {nowStr}</div>");
            html.AppendLine($"   <div>Total Vouchers Synchronized: {TotalVouchersSynchronized}</div>");
            html.AppendLine($"   <div>Total Exceptions: {allExceptions.Count}</div>");
            html.AppendLine(" </div>");

            html.AppendLine(" <div class='card'>");
            html.AppendLine("   <h2>Exceptions Summary</h2>");
            html.AppendLine("   <table>");
            html.AppendLine("     <tr><th>Rule Name</th><th>Category</th><th>Severity</th><th>Voucher No</th><th>Ledger Name</th><th>Amount</th><th>Status</th></tr>");
            foreach (var ex in allExceptions)
            {
                var vNo = ex.VoucherNumber ?? "—";
                var lName = ex.LedgerName ?? "—";
                var amt = ex.FlaggedAmount?.ToString("C") ?? "—";
                html.AppendLine("     <tr>");
                html.AppendLine($"       <td><strong>{ex.RuleName}</strong></td>");
                html.AppendLine($"       <td>{ex.Category}</td>");
                html.AppendLine($"       <td>{ex.Severity}</td>");
                html.AppendLine($"       <td>{vNo}</td>");
                html.AppendLine($"       <td>{lName}</td>");
                html.AppendLine($"       <td>{amt}</td>");
                html.AppendLine($"       <td>{ex.Status}</td>");
                html.AppendLine("     </tr>");
            }
            html.AppendLine("   </table>");
            html.AppendLine(" </div>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"Audit_Executive_Summary_{current.TallyCompanyName.Replace(" ", "_")}.html");
            await File.WriteAllTextAsync(path, html.ToString(), Encoding.UTF8);
            
            StatusMessage = $"Summary report exported successfully to {path}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"PDF generation failed: {ex.Message}";
        }
    }
}
