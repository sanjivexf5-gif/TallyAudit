using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Common;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data.Repositories;

namespace TallyAuditAssistant.Engine.Services;

public class AuditTrailService : IAuditTrailService
{
    private readonly IAuditTrailRepository _repository;
    private readonly IActiveCompanyContext? _companyContext;
    private readonly ILogger<AuditTrailService> _logger;

    private static readonly Regex SensitivePatterns = new(
        @"(password|passwd|token|apikey|secret|credential|authorization)\s*[:=]\s*[^\s,;]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public AuditTrailService(
        IAuditTrailRepository repository,
        ILogger<AuditTrailService> logger,
        IActiveCompanyContext? companyContext = null)
    {
        _repository = repository;
        _logger = logger;
        _companyContext = companyContext;
    }

    public async Task RecordActivityAsync(
        string actionType,
        string module,
        string description,
        string? entityType = null,
        string? entityId = null,
        string? previousState = null,
        string? newState = null,
        string? details = null,
        string? companyName = null,
        string? financialYear = null,
        CancellationToken ct = default)
    {
        try
        {
            var timestamp = DateTime.UtcNow;
            var userName = Environment.UserName;
            if (string.IsNullOrWhiteSpace(userName))
            {
                userName = "Auditor";
            }

            // Fallback to active company context if not explicitly provided
            var effectiveCompany = !string.IsNullOrWhiteSpace(companyName)
                ? companyName.Trim()
                : (_companyContext?.TallyCompanyName ?? _companyContext?.ActiveCompanyName);

            var effectiveFy = !string.IsNullOrWhiteSpace(financialYear)
                ? financialYear.Trim()
                : _companyContext?.ActiveFinancialYear;

            // Sanitize sensitive strings
            var sanitizedDescription = Sanitize(description) ?? string.Empty;
            var sanitizedDetails = Sanitize(details);
            var sanitizedPrevState = Sanitize(previousState);
            var sanitizedNewState = Sanitize(newState);

            var entryId = $"LOG-{timestamp:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..6]}";

            // Compute deterministic SHA256 integrity hash
            string rawHashString = $"{entryId}|{timestamp:O}|{userName}|{actionType}|{module}|{effectiveCompany}|{effectiveFy}|{entityType}|{entityId}|{sanitizedDescription}";
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawHashString));
            string integrityHash = "SHA256-" + Convert.ToHexString(hashBytes)[..16];

            var entry = new AuditTrailEntry
            {
                Id = entryId,
                TimestampUtc = timestamp,
                UserName = userName,
                ActionType = actionType,
                Module = module,
                CompanyName = effectiveCompany,
                FinancialYear = effectiveFy,
                EntityType = entityType,
                EntityId = entityId,
                PreviousState = sanitizedPrevState,
                NewState = sanitizedNewState,
                Description = sanitizedDescription,
                Details = sanitizedDetails,
                ApplicationVersion = AppVersion.Version,
                MachineName = Environment.MachineName,
                IntegrityHash = integrityHash
            };

            await _repository.InsertAsync(entry, ct);
        }
        catch (Exception ex)
        {
            // The primary audit operation remains authoritative; do not fail caller
            _logger.LogError(ex, "Failed to persist audit trail record for action '{ActionType}' in module '{Module}'.", actionType, module);
        }
    }

    public Task<IReadOnlyList<AuditTrailEntry>> GetEntriesAsync(
        string? searchTerm = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        string? companyName = null,
        string? module = null,
        string? actionType = null,
        string? userName = null,
        int limit = 1000,
        CancellationToken ct = default)
    {
        return _repository.GetEntriesAsync(
            searchTerm,
            fromUtc,
            toUtc,
            companyName,
            module,
            actionType,
            userName,
            limit,
            ct);
    }

    public Task<IReadOnlyList<AuditTrailEntry>> GetAuditTrailAsync(
        string? companyId = null,
        string? financialPeriodId = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        return _repository.GetEntriesAsync(
            searchTerm: null,
            fromUtc: null,
            toUtc: null,
            companyName: companyId,
            module: null,
            actionType: null,
            userName: null,
            limit: limit,
            ct: ct);
    }

    public Task<byte[]> ExportToExcelAsync(IReadOnlyList<AuditTrailEntry> entries, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        // CSV with UTF-8 BOM for universal Excel compatibility
        sb.AppendLine("ID,Timestamp (UTC),User Name,Action Type,Module,Company Name,Financial Year,Entity Type,Entity ID,Previous State,New State,Description,Details,App Version,Machine Name");

        foreach (var e in entries)
        {
            sb.AppendLine(string.Join(",",
                EscapeCsv(e.Id),
                EscapeCsv(e.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss")),
                EscapeCsv(e.UserName),
                EscapeCsv(e.ActionType),
                EscapeCsv(e.Module),
                EscapeCsv(e.CompanyName ?? string.Empty),
                EscapeCsv(e.FinancialYear ?? string.Empty),
                EscapeCsv(e.EntityType ?? string.Empty),
                EscapeCsv(e.EntityId ?? string.Empty),
                EscapeCsv(e.PreviousState ?? string.Empty),
                EscapeCsv(e.NewState ?? string.Empty),
                EscapeCsv(e.Description),
                EscapeCsv(e.Details ?? string.Empty),
                EscapeCsv(e.ApplicationVersion),
                EscapeCsv(e.MachineName)
            ));
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var dataBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[preamble.Length + dataBytes.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(dataBytes, 0, result, preamble.Length, dataBytes.Length);

        return Task.FromResult(result);
    }

    public Task<byte[]> ExportToPdfAsync(IReadOnlyList<AuditTrailEntry> entries, CancellationToken ct = default)
    {
        // Generate formatted text/PDF report stream
        var sb = new StringBuilder();
        sb.AppendLine("==========================================================================================================");
        sb.AppendLine("                                TALLY AUDIT ASSISTANT — STATUTORY AUDIT TRAIL");
        sb.AppendLine($" Generated At (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} | Application Version: {AppVersion.Version} | Total Records: {entries.Count}");
        sb.AppendLine("==========================================================================================================");
        sb.AppendLine();

        foreach (var e in entries)
        {
            sb.AppendLine($"[{e.TimestampUtc:yyyy-MM-dd HH:mm:ss}] [{e.Module}] {e.ActionType}");
            sb.AppendLine($"  User: {e.UserName} | Machine: {e.MachineName} | Version: {e.ApplicationVersion}");
            if (!string.IsNullOrWhiteSpace(e.CompanyName))
            {
                sb.AppendLine($"  Company: {e.CompanyName} | FY: {e.FinancialYear ?? "N/A"}");
            }
            if (!string.IsNullOrWhiteSpace(e.EntityType) || !string.IsNullOrWhiteSpace(e.EntityId))
            {
                sb.AppendLine($"  Entity: {e.EntityType ?? "N/A"} #{e.EntityId ?? "N/A"}");
            }
            if (!string.IsNullOrWhiteSpace(e.PreviousState) || !string.IsNullOrWhiteSpace(e.NewState))
            {
                sb.AppendLine($"  State Change: '{e.PreviousState ?? "None"}' -> '{e.NewState ?? "None"}'");
            }
            sb.AppendLine($"  Description: {e.Description}");
            if (!string.IsNullOrWhiteSpace(e.Details))
            {
                sb.AppendLine($"  Details: {e.Details}");
            }
            sb.AppendLine(new string('-', 106));
        }

        return Task.FromResult(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static string? Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return SensitivePatterns.Replace(input, "$1: [REDACTED]");
    }

    private static string EscapeCsv(string field)
    {
        if (string.IsNullOrEmpty(field)) return "\"\"";
        return "\"" + field.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
    }
}

