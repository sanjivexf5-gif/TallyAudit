using System.Text;
using Dapper;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;

namespace TallyAuditAssistant.Engine.Services;

public class TallyDrillDownService : ITallyDrillDownService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ITallyClient _tallyClient;
    private readonly ILogger<TallyDrillDownService> _logger;

    public TallyDrillDownService(
        ISqliteConnectionFactory connectionFactory,
        ITallyClient tallyClient,
        ILogger<TallyDrillDownService> logger)
    {
        _connectionFactory = connectionFactory;
        _tallyClient = tallyClient;
        _logger = logger;
    }

    public async Task<TallyVoucherDrillDownInfo?> GetVoucherDrillDownAsync(
        string companyId, 
        string voucherId, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            if (connection == null)
            {
                _logger.LogWarning("Connection factory returned null connection for company {CompanyId}.", companyId);
                return null;
            }

            const string voucherSql = @"
                SELECT 
                    v.Id AS VoucherId,
                    v.CompanyId,
                    c.TallyCompanyName AS CompanyName,
                    v.VoucherNumber,
                    v.VoucherTypeName,
                    v.VoucherDate,
                    v.ReferenceNumber,
                    v.PartyLedgerName,
                    v.TotalAmount,
                    v.Narration,
                    v.AlterId
                FROM Vouchers v
                LEFT JOIN Companies c ON v.CompanyId = c.Id
                WHERE v.Id = @VoucherId OR v.VoucherNumber = @VoucherId";

            var voucher = await connection.QueryFirstOrDefaultAsync<dynamic>(
                new CommandDefinition(voucherSql, new { VoucherId = voucherId }, cancellationToken: cancellationToken));

            if (voucher == null)
            {
                _logger.LogWarning("Voucher {VoucherId} not found in local SQLite database for company {CompanyId}", voucherId, companyId);
                return null;
            }

            const string entriesSql = @"
                SELECT 
                    ve.Id AS EntryId,
                    ve.LedgerName,
                    l.ParentGroup,
                    ve.Amount,
                    ve.IsDebit,
                    l.HsnCode AS HsnOrSac,
                    l.GstRate AS TaxOrTdsRate
                FROM VoucherEntries ve
                LEFT JOIN Ledgers l ON l.CompanyId = @CompanyId AND l.Name = ve.LedgerName
                WHERE ve.VoucherId = @VoucherId";

            var entries = (await connection.QueryAsync<TallyVoucherLinePosting>(
                new CommandDefinition(entriesSql, new { CompanyId = companyId, VoucherId = (string)voucher.VoucherId }, cancellationToken: cancellationToken))).ToList();

            DateTime vDate = DateTime.TryParse((string)voucher.VoucherDate, out DateTime dt) ? dt : DateTime.Today;
            string companyName = (string)voucher.CompanyName ?? "Active Company";
            string voucherNumber = (string)voucher.VoucherNumber ?? voucherId;
            string voucherTypeName = (string)voucher.VoucherTypeName ?? "Journal";
            string? masterId = null;

            var navGuide = GenerateNavigationGuide(companyName, voucherNumber, voucherTypeName, vDate, masterId);

            var result = new TallyVoucherDrillDownInfo
            {
                CompanyGuid = (string)voucher.CompanyId,
                CompanyName = companyName,
                VoucherId = (string)voucher.VoucherId,
                VoucherNumber = voucherNumber,
                VoucherTypeName = voucherTypeName,
                VoucherDate = vDate,
                ReferenceNumber = (string?)voucher.ReferenceNumber,
                PartyLedgerName = (string)voucher.PartyLedgerName ?? "Party",
                TotalAmount = (decimal)(voucher.TotalAmount ?? 0m),
                Narration = (string?)voucher.Narration,
                MasterId = masterId ?? string.Empty,
                AlterId = (string?)voucher.AlterId ?? string.Empty,
                Entries = entries,
                NavigationGuide = navGuide
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve voucher drill-down info for voucher {VoucherId}.", voucherId);
            return null;
        }
    }

    public async Task<TallyOpenAttemptResult> AttemptOpenInTallyAsync(
        string companyId, 
        string voucherId, 
        CancellationToken cancellationToken = default)
    {
        TallyVoucherDrillDownInfo? voucherInfo = null;
        try
        {
            voucherInfo = await GetVoucherDrillDownAsync(companyId, voucherId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Drill-down lookup failed for voucher {VoucherId}.", voucherId);
        }

        var navGuide = voucherInfo?.NavigationGuide ?? GenerateNavigationGuide("Active Company", voucherId, "Voucher", DateTime.Today);

        // Verify Tally Server Connectivity over configured XML Port (default 9000)
        bool isConnected = false;
        try
        {
            isConnected = await _tallyClient.TestConnectionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tally XML client connection check failed during drill-down attempt.");
            isConnected = false;
        }

        if (!isConnected)
        {
            return new TallyOpenAttemptResult
            {
                IsDirectLaunchSuccess = false,
                MethodAttempted = "TallyPrime XML Server Protocol Verification",
                RequiresManualNavigationFallback = true,
                StatusMessage = "TallyPrime XML Server is not responding on Port 9000. Use manual keyboard navigation below.",
                NavigationGuide = navGuide
            };
        }

        // TallyPrime XML Server responds over HTTP/XML but standard stock Tally does not register an OS URI scheme (e.g. tally://).
        // Therefore, we execute verified programmatic voucher lookup, format the TDL search query, and provide verified manual breadcrumbs.
        return new TallyOpenAttemptResult
        {
            IsDirectLaunchSuccess = false,
            MethodAttempted = "TallyPrime XML Server API Verification (Port 9000 Connected)",
            RequiresManualNavigationFallback = true,
            StatusMessage = "Verified active TallyPrime connection. Stock TallyPrime requires keyboard navigation to alter vouchers. All identifiers copied to clipboard.",
            NavigationGuide = navGuide
        };
    }

    public TallyNavigationBreadcrumb GenerateNavigationGuide(
        string companyName,
        string voucherNumber,
        string voucherTypeName,
        DateTime voucherDate,
        string? masterId = null)
    {
        string formattedDate = voucherDate.ToString("dd-MMM-yyyy");
        string dayBookDate = voucherDate.ToString("dd-MM-yyyy");

        var sbXml = new StringBuilder();
        sbXml.AppendLine("<ENVELOPE>");
        sbXml.AppendLine("  <HEADER>");
        sbXml.AppendLine("    <TALLYREQUEST>Export Data</TALLYREQUEST>");
        sbXml.AppendLine("  </HEADER>");
        sbXml.AppendLine("  <BODY>");
        sbXml.AppendLine("    <EXPORTDATA>");
        sbXml.AppendLine("      <REQUESTDESC>");
        sbXml.AppendLine($"        <REPORTNAME>Voucher Register</REPORTNAME>");
        sbXml.AppendLine("        <STATICVARIABLES>");
        sbXml.AppendLine($"          <SVCURRENTCOMPANY>{companyName}</SVCURRENTCOMPANY>");
        sbXml.AppendLine($"          <SVFROMDATE>{dayBookDate}</SVFROMDATE>");
        sbXml.AppendLine($"          <SVTODATE>{dayBookDate}</SVTODATE>");
        sbXml.AppendLine($"          <VOUCHERTYPENAME>{voucherTypeName}</VOUCHERTYPENAME>");
        if (!string.IsNullOrEmpty(masterId))
        {
            sbXml.AppendLine($"          <VOUCHERMASTERID>{masterId}</VOUCHERMASTERID>");
        }
        sbXml.AppendLine("        </STATICVARIABLES>");
        sbXml.AppendLine("      </REQUESTDESC>");
        sbXml.AppendLine("    </EXPORTDATA>");
        sbXml.AppendLine("  </BODY>");
        sbXml.AppendLine("</ENVELOPE>");

        return new TallyNavigationBreadcrumb
        {
            GatewayPath = $"Gateway of Tally > Display More Reports (D) > Day Book (D) > Change Date [Alt+F2: {formattedDate}] > Select {voucherTypeName} #{voucherNumber}",
            KeyboardShortcuts = $"Alt+G (Go To) > Type \"{voucherNumber}\" or Day Book (D) > Alt+F2 ({formattedDate})",
            GoToSearchQuery = voucherNumber,
            DayBookFilterDate = formattedDate,
            TdlXmlPayload = sbXml.ToString()
        };
    }
}
