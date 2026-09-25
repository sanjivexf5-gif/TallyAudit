using Microsoft.Data.Sqlite;

namespace TallyAuditAssistant.Core.Interfaces;

public interface ISqliteConnectionFactory
{
    Task<SqliteConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
    SqliteConnection CreateConnection();
}
