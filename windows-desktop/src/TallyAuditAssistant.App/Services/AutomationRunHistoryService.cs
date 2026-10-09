using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TallyAuditAssistant.App.Services;

/// <summary>
/// Stores a bounded audit-automation run history on the local machine.
/// No accounting records or run data are sent to an external service.
/// </summary>
public sealed class AutomationRunHistoryService
{
    private const int MaximumEntries = 100;
    private readonly string _historyPath;
    private readonly object _gate = new();

    public AutomationRunHistoryService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TallyAuditAssistant");
        Directory.CreateDirectory(directory);
        _historyPath = Path.Combine(directory, "AutomationRunHistory.json");
    }

    public IReadOnlyList<AutomationRunHistoryEntry> GetRecentRuns(int count = 10)
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_historyPath))
                {
                    return Array.Empty<AutomationRunHistoryEntry>();
                }

                var entries = JsonSerializer.Deserialize<List<AutomationRunHistoryEntry>>(
                    File.ReadAllText(_historyPath)) ?? new List<AutomationRunHistoryEntry>();
                return entries
                    .OrderByDescending(entry => entry.StartedAtLocal)
                    .Take(Math.Clamp(count, 1, MaximumEntries))
                    .ToArray();
            }
            catch (IOException)
            {
                return Array.Empty<AutomationRunHistoryEntry>();
            }
            catch (JsonException)
            {
                return Array.Empty<AutomationRunHistoryEntry>();
            }
        }
    }

    public void RecordRun(AutomationRunHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            List<AutomationRunHistoryEntry> entries;
            try
            {
                entries = File.Exists(_historyPath)
                    ? JsonSerializer.Deserialize<List<AutomationRunHistoryEntry>>(File.ReadAllText(_historyPath))
                        ?? new List<AutomationRunHistoryEntry>()
                    : new List<AutomationRunHistoryEntry>();
            }
            catch (JsonException)
            {
                // Preserve the previous file for diagnosis; start a fresh history if it is malformed.
                var backup = _historyPath + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".corrupt";
                try { File.Move(_historyPath, backup, overwrite: true); } catch (IOException) { }
                entries = new List<AutomationRunHistoryEntry>();
            }

            entries.Add(entry);
            entries = entries
                .OrderByDescending(item => item.StartedAtLocal)
                .Take(MaximumEntries)
                .ToList();

            var temporaryPath = _historyPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _historyPath, overwrite: true);
        }
    }
}

public sealed record AutomationRunHistoryEntry(
    DateTime StartedAtLocal,
    string CompanyName,
    string Trigger,
    string Status,
    int RecordsSynchronized,
    int FindingsGenerated,
    double DurationSeconds,
    string? Details = null)
{
    public string StartedAtDisplay => StartedAtLocal.ToString("yyyy-MM-dd HH:mm:ss");
    public string DurationDisplay => TimeSpan.FromSeconds(Math.Max(0, DurationSeconds)).ToString(@"hh\:mm\:ss");
}
