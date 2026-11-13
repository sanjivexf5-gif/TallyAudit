using Dapper;
using Microsoft.Data.Sqlite;
using System.Data.Common;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data;

public class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _journalModeLock = new(1, 1);
    private bool _journalModeInitialized;

    public string DatabasePath { get; }

    static SqliteConnectionFactory()
    {
        SqlMapper.AddTypeHandler(SqliteDecimalHandler.Instance);
        SqlMapper.AddTypeHandler(SqliteNullableDecimalHandler.Instance);
    }

    public SqliteConnectionFactory(string databasePath)
    {
        DatabasePath = databasePath;
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private
        };
        _connectionString = builder.ToString();
    }

    public async Task<SqliteConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        // WAL mode is database-wide and persistent. Set it once per factory rather
        // than executing a journal-mode transition for every short-lived connection.
        await _journalModeLock.WaitAsync(cancellationToken);
        try
        {
            var connection = new SqliteConnection(_connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken);
                if (!_journalModeInitialized)
                {
                    await ExecutePragmaAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
                    _journalModeInitialized = true;
                }

                await ConfigureConnectionAsync(connection, cancellationToken);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _journalModeLock.Release();
        }
    }

    public SqliteConnection CreateConnection()
    {
        _journalModeLock.Wait();
        try
        {
            var connection = new SqliteConnection(_connectionString);
            try
            {
                connection.Open();
                if (!_journalModeInitialized)
                {
                    ExecutePragma(connection, "PRAGMA journal_mode = WAL;");
                    _journalModeInitialized = true;
                }

                ExecutePragma(connection, @"
                    PRAGMA synchronous = NORMAL;
                    PRAGMA foreign_keys = ON;
                    PRAGMA busy_timeout = 5000;
                ");
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
        finally
        {
            _journalModeLock.Release();
        }
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await ExecutePragmaAsync(connection, @"
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 30000;
        ", cancellationToken);
    }

    private static async Task ExecutePragmaAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ExecutePragma(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }
}
