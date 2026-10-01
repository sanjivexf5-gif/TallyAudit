namespace TallyAuditAssistant.Core.Interfaces;

/// <summary>
/// Central provider for application storage paths (AppData, SQLite DB, Logs).
/// Guarantees that production database is located in %LOCALAPPDATA% and accessible under standard Windows permissions.
/// </summary>
public interface IApplicationDataPathService
{
    string ApplicationDataDirectory { get; }
    string DatabasePath { get; }
    string LogsDirectory { get; }
}
