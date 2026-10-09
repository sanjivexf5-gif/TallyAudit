using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;

namespace TallyAuditAssistant.App.Services;

/// <summary>
/// Registers a per-user Windows Task Scheduler job. The job launches the app in
/// a non-interactive scheduled-audit mode, so the main window is not required.
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

        var taskCommand = $"\\"{executablePath}\\" --scheduled-audit";
        var arguments = new System.Collections.Generic.List<string> { "/Create" };
        arguments.AddRange(schedule);
        arguments.AddRange(new[] { "/ST", time, "/TN", TaskName, "/TR", taskCommand, "/F", "/IT" });

        await RunSchtasksAsync(arguments);
    }

    public async Task RemoveAsync()
    {
        await RunSchtasksAsync(new[] { "/Delete", "/TN", TaskName, "/F" });
    }

    private static async Task RunSchtasksAsync(System.Collections.Generic.IEnumerable<string> arguments)
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
    }
}
