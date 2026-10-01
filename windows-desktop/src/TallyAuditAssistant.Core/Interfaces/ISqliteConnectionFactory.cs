using Microsoft.Data.Sqlite;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ISqliteConnectionFactory
{
    string DatabasePath { get; }
    Task<SqliteConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
    SqliteConnection CreateConnection();
}
