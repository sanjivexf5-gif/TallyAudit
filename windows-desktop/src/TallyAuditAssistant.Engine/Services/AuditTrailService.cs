using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
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
                MachineName = Environment.MachineName
            };
            entry.IntegrityHash = AuditTrailIntegrity.ComputeHash(entry);

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
        sb.AppendLine("ID,Timestamp (UTC),User Name,Action Type,Module,Company Name,Financial Year,Entity Type,Entity ID,Previous State,New State,Description,Details,App Version,Machine Name,Integrity Hash");

        foreach (var e in entries)
        {
            sb.AppendLine(string.Join(",",
                EscapeCsv(e.Id),
                EscapeCsv(e.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
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
                EscapeCsv(e.MachineName),
                EscapeCsv(e.IntegrityHash)
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
        ArgumentNullException.ThrowIfNull(entries);
        ct.ThrowIfCancellationRequested();
        EnsurePdfSharpFontSettings();

        using var document = new PdfDocument();
        document.Info.Title = "Tally Audit Assistant - Statutory Audit Trail";
        document.Info.Author = "Tally Audit Assistant";
        document.Info.Subject = "Audit trail event report including record integrity hashes";

        var titleFont = new XFont("Arial", 14, XFontStyleEx.Bold);
        var headerFont = new XFont("Arial", 9, XFontStyleEx.Bold);
        var bodyFont = new XFont("Arial", 9, XFontStyleEx.Regular);
        using var stream = new MemoryStream();

        const double margin = 40;
        PdfPage? page = null;
        XGraphics? graphics = null;
        var pageNumber = 0;
        var y = 0d;
        var contentWidth = 0d;

        void StartPage()
        {
            page = document.AddPage();
            page.Size = PageSize.A4;
            graphics = XGraphics.FromPdfPage(page);
            pageNumber++;
            contentWidth = page.Width.Point - (2 * margin);
            y = margin;

            var heading = pageNumber == 1
                ? "TALLY AUDIT ASSISTANT - STATUTORY AUDIT TRAIL"
                : "STATUTORY AUDIT TRAIL (CONTINUED)";
            graphics.DrawString(heading, titleFont, XBrushes.DarkBlue,
                new XRect(margin, y, contentWidth, 24), XStringFormats.TopLeft);
            y += 28;
            graphics.DrawLine(XPens.LightGray, margin, y, page.Width.Point - margin, y);
            y += 12;
        }

        void DrawFooter()
        {
            if (page == null || graphics == null) return;
            graphics.DrawString($"Page {pageNumber}", bodyFont, XBrushes.Gray,
                new XRect(margin, page.Height.Point - margin + 4, contentWidth, 14), XStringFormats.TopRight);
        }

        void DrawWrappedLine(string value, XFont? chosenFont = null)
        {
            var font = chosenFont ?? bodyFont;
            // Materialize wrapping before drawing: moving to a new page disposes the old
            // XGraphics instance, so lazy measurement must not continue against it.
            var wrappedLines = WrapPdfLine(value, font, graphics!, contentWidth);
            foreach (var wrappedLine in wrappedLines)
            {
                ct.ThrowIfCancellationRequested();
                var lineHeight = font.Size + 4;
                if (y + lineHeight > page!.Height.Point - margin - 18)
                {
                    DrawFooter();
                    graphics!.Dispose();
                    StartPage();
                }

                graphics!.DrawString(wrappedLine, font, XBrushes.Black,
                    new XRect(margin, y, contentWidth, lineHeight), XStringFormats.TopLeft);
                y += lineHeight;
            }
        }

        StartPage();
        DrawWrappedLine($"Generated at (UTC): {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        DrawWrappedLine($"Application version: {AppVersion.Version}");
        DrawWrappedLine($"Total records: {entries.Count}");
        DrawWrappedLine(new string('=', 82));

        if (entries.Count == 0)
        {
            DrawWrappedLine("No audit trail records were selected.");
        }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            DrawWrappedLine(
                $"[{entry.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}] [{entry.Module}] {entry.ActionType}",
                headerFont);
            DrawWrappedLine($"  Record ID: {entry.Id}");
            DrawWrappedLine($"  User: {entry.UserName} | Machine: {entry.MachineName} | Version: {entry.ApplicationVersion}");
            if (!string.IsNullOrWhiteSpace(entry.CompanyName))
                DrawWrappedLine($"  Company: {entry.CompanyName} | Financial year: {entry.FinancialYear ?? "N/A"}");
            if (!string.IsNullOrWhiteSpace(entry.EntityType) || !string.IsNullOrWhiteSpace(entry.EntityId))
                DrawWrappedLine($"  Entity: {entry.EntityType ?? "N/A"} #{entry.EntityId ?? "N/A"}");
            if (!string.IsNullOrWhiteSpace(entry.PreviousState) || !string.IsNullOrWhiteSpace(entry.NewState))
                DrawWrappedLine($"  State change: '{entry.PreviousState ?? "None"}' -> '{entry.NewState ?? "None"}'");
            DrawWrappedLine($"  Description: {entry.Description}");
            if (!string.IsNullOrWhiteSpace(entry.Details))
                DrawWrappedLine($"  Details: {entry.Details}");
            DrawWrappedLine($"  Integrity hash: {entry.IntegrityHash}");
            DrawWrappedLine(new string('-', 82));
        }

        DrawFooter();
        graphics?.Dispose();
        document.Save(stream, false);
        return Task.FromResult(stream.ToArray());
    }

    private static List<string> WrapPdfLine(string value, XFont font, XGraphics graphics, double maxWidth)
    {
        var output = new List<string>();
        var normalized = (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var paragraph in normalized.Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                output.Add(string.Empty);
                continue;
            }

            var words = paragraph.Split(' ', StringSplitOptions.None);
            var current = new StringBuilder();
            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : current.ToString() + " " + word;
                if (graphics.MeasureString(candidate, font).Width <= maxWidth)
                {
                    if (current.Length > 0) current.Append(' ');
                    current.Append(word);
                    continue;
                }

                if (current.Length > 0)
                {
                    output.Add(current.ToString());
                    current.Clear();
                }

                if (graphics.MeasureString(word, font).Width <= maxWidth)
                {
                    current.Append(word);
                    continue;
                }

                var part = new StringBuilder();
                foreach (var character in word)
                {
                    var next = part.ToString() + character;
                    if (part.Length > 0 && graphics.MeasureString(next, font).Width > maxWidth)
                    {
                        output.Add(part.ToString());
                        part.Clear();
                    }
                    part.Append(character);
                }
                if (part.Length > 0) current.Append(part);
            }
            if (current.Length > 0) output.Add(current.ToString());
        }
        return output;
    }

    private static readonly object PdfFontSettingsLock = new();
    private static bool _pdfFontSettingsInitialized;

    private static void EnsurePdfSharpFontSettings()
    {
        lock (PdfFontSettingsLock)
        {
            if (_pdfFontSettingsInitialized) return;
            // This product targets Windows; PDFsharp's Core build needs the Windows font resolver before XFont creation.
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            _pdfFontSettingsInitialized = true;
        }
    }

    private static string? Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return SensitivePatterns.Replace(input, "$1: [REDACTED]");
    }

    private static string EscapeCsv(string? field)
    {
        var normalized = field ?? string.Empty;
        var trimmed = normalized.TrimStart();
        var formulaLike = trimmed.Length > 0 &&
                          (trimmed[0] == '=' || trimmed[0] == '+' ||
                           trimmed[0] == '-' || trimmed[0] == '@');
        var startsWithControl = normalized.Length > 0 &&
                                (normalized[0] == '\t' || normalized[0] == '\r' || normalized[0] == '\n');

        // CSV quoting alone does not prevent spreadsheet formula execution.
        if (formulaLike || startsWithControl) normalized = "'" + normalized;
        return "\"" + normalized.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
    }
}

