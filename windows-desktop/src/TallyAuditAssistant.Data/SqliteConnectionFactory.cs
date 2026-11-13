using Dapper;
using Microsoft.Data.Sqlite;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.Data;

public class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _journalModeLock = new(1, 1);
    private volatile bool _journalModeInitialized;

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
        if (!_journalModeInitialized)
        {
            await _journalModeLock.WaitAsync(cancellationToken);
            try
            {
                if (!_journalModeInitialized)
                {
                    await using var initializer = new SqliteConnection(_connectionString);
                    await initializer.OpenAsync(cancellationToken);
                    await ExecutePragmaAsync(initializer, "PRAGMA journal_mode = WAL;", cancellationToken);
                    _journalModeInitialized = true;
                }
            }
            finally
            {
                _journalModeLock.Release();
            }
        }

        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await ConfigureConnectionAsync(connection, cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public SqliteConnection CreateConnection()
    {
        if (!_journalModeInitialized)
        {
            _journalModeLock.Wait();
            try
            {
                if (!_journalModeInitialized)
                {
                    using var initializer = new SqliteConnection(_connectionString);
                    initializer.Open();
                    ExecutePragma(initializer, "PRAGMA journal_mode = WAL;");
                    _journalModeInitialized = true;
                }
            }
            finally
            {
                _journalModeLock.Release();
            }
        }

        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
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
