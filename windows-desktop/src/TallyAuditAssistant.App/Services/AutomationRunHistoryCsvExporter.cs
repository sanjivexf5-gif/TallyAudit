using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text;

namespace TallyAuditAssistant.App.Services;

/// <summary>
/// Exports the locally retained automation run history to an auditor-selected CSV file.
/// Text cells are CSV-escaped and protected against spreadsheet formula injection.
/// </summary>
public static class AutomationRunHistoryCsvExporter
{
    private const string Header =
        "Started At,Company,Trigger,Status,Records Synchronized,Findings Generated,Duration Seconds,Details";

    public static string ToCsv(IEnumerable<AutomationRunHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var builder = new StringBuilder();
        builder.Append(Header).Append("\r\n");

        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            var fields = new[]
            {
                entry.StartedAtLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                entry.CompanyName,
                entry.Trigger,
                entry.Status,
                entry.RecordsSynchronized.ToString(CultureInfo.InvariantCulture),
                entry.FindingsGenerated.ToString(CultureInfo.InvariantCulture),
                entry.DurationSeconds.ToString("0.##", CultureInfo.InvariantCulture),
                entry.Details ?? string.Empty
            };

            builder.Append(string.Join(",", fields.Select(EscapeField))).Append("\r\n");
        }

        return builder.ToString();
    }

    public static void WriteToFile(string filePath, IEnumerable<AutomationRunHistoryEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A destination file path is required.", nameof(filePath));
        }

        var csv = ToCsv(entries);
        File.WriteAllText(filePath, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string EscapeField(string? value)
    {
        var normalized = value ?? string.Empty;
        var trimmed = normalized.TrimStart();
        var formulaLike = trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@';
        var startsWithControl = normalized.Length > 0 && normalized[0] is '\t' or '\r' or '\n';

        // Quoting alone does not prevent Excel from interpreting a cell as a formula.
        if (formulaLike || startsWithControl)
        {
            normalized = "'" + normalized;
        }

        if (normalized.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
        {
            return "\"" + normalized.Replace("\"", "\"\"") + "\"";
        }

        return normalized;
    }
}
