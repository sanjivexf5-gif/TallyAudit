using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TallyAuditAssistant.App.Services;

/// <summary>
/// Registers and inspects the per-user Windows Task Scheduler job used for
/// unattended audit runs.
/// </summary>
public sealed class ScheduledAuditTaskService
{
    public const string TaskName = "Tally Audit Assistant - Scheduled Audit";

    public async Task CreateOrUpdateAsync(string frequency, string time)
    {
        if (!TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException("Enter the run time in 24-hour HH:mm format, for example 02:00.", nameof(time));
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Could not determine the installed Tally Audit Assistant executable path.");
        }

        var schedule = string.Equals(frequency, "Weekly (Monday)", StringComparison.OrdinalIgnoreCase)
            ? new[] { "/SC", "WEEKLY", "/D", "MON" }
            : new[] { "/SC", "DAILY" };

        var taskCommand = $"\"{executablePath}\" --scheduled-audit";
        var arguments = new System.Collections.Generic.List<string> { "/Create" };
        arguments.AddRange(schedule);
        arguments.AddRange(new[] { "/ST", time, "/TN", TaskName, "/TR", taskCommand, "/F", "/IT" });

        await RunSchtasksAsync(arguments);
    }

    public async Task RemoveAsync()
    {
        await RunSchtasksAsync(new[] { "/Delete", "/TN", TaskName, "/F" });
    }

    /// <summary>Reads the existing task definition so the UI reflects schedules after restart.</summary>
    public async Task<ScheduledAuditConfiguration?> GetScheduleAsync()
    {
        try
        {
            var xml = await RunSchtasksAsync(new[] { "/Query", "/TN", TaskName, "/XML" });
            var document = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var trigger = document.Descendants(ns + "CalendarTrigger").FirstOrDefault();
            var startBoundary = trigger?.Element(ns + "StartBoundary")?.Value
                ?? document.Descendants(ns + "StartBoundary").FirstOrDefault()?.Value;

            if (string.IsNullOrWhiteSpace(startBoundary) ||
                !DateTimeOffset.TryParse(startBoundary, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var start))
            {
                return new ScheduledAuditConfiguration("Daily", "02:00");
            }

            var isWeekly = trigger?.Element(ns + "ScheduleByWeek") is not null;
            var frequency = isWeekly ? "Weekly (Monday)" : "Daily";
            return new ScheduledAuditConfiguration(frequency, start.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Xml.XmlException or FormatException)
        {
            // A missing task is normal for a first-time user; surface other errors
            // through the schedule status without preventing the automation view from opening.
            if (ex.Message.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            throw;
        }
    }

    private static async Task<string> RunSchtasksAsync(System.Collections.Generic.IEnumerable<string> arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("Windows Task Scheduler could not be started.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(detail)
                    ? $"Windows Task Scheduler returned exit code {process.ExitCode}."
                    : detail.Trim());
        }

        return standardOutput;
    }
}

public sealed record ScheduledAuditConfiguration(string Frequency, string Time);
