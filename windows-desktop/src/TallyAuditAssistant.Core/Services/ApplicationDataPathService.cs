using System;
using System.IO;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Core.Services;

/// <summary>
/// Production path provider ensuring SQLite data is safely stored in %LOCALAPPDATA%\TallyAuditAssistant\Data\audit_assistant_data.db
/// with automatic migration of any legacy database file from the application installation directory.
/// </summary>
public class ApplicationDataPathService : IApplicationDataPathService
{
    public string ApplicationDataDirectory { get; }
    public string DatabasePath { get; }
    public string LogsDirectory { get; }

    public ApplicationDataPathService(string? customDbPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customDbPath))
        {
            DatabasePath = Path.GetFullPath(customDbPath);
            ApplicationDataDirectory = Path.GetDirectoryName(DatabasePath) ?? string.Empty;
            LogsDirectory = Path.Combine(ApplicationDataDirectory, "Logs");
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = AppDomain.CurrentDomain.BaseDirectory;
            }

            ApplicationDataDirectory = Path.Combine(localAppData, "TallyAuditAssistant", "Data");
            DatabasePath = Path.Combine(ApplicationDataDirectory, "audit_assistant_data.db");
            LogsDirectory = Path.Combine(localAppData, "TallyAuditAssistant", "Logs");
        }

        EnsureDirectoriesExist();
        MigrateLegacyDatabaseIfPresent();
    }

    private void EnsureDirectoriesExist()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(ApplicationDataDirectory) && !Directory.Exists(ApplicationDataDirectory))
            {
                Directory.CreateDirectory(ApplicationDataDirectory);
            }
            if (!string.IsNullOrWhiteSpace(LogsDirectory) && !Directory.Exists(LogsDirectory))
            {
                Directory.CreateDirectory(LogsDirectory);
            }
        }
        catch
        {
            // Directory creation fallback handled by callers if needed
        }
    }

    private void MigrateLegacyDatabaseIfPresent()
    {
        try
        {
            if (File.Exists(DatabasePath))
            {
                return;
            }

            var legacyDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audit_assistant_data.db");
            if (File.Exists(legacyDbPath) && !string.Equals(legacyDbPath, DatabasePath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(legacyDbPath, DatabasePath, overwrite: false);
            }
        }
        catch
        {
            // Best effort legacy migration
        }
    }
}
