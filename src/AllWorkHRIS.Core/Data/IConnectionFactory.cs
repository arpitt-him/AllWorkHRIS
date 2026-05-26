// AllWorkHRIS.Core/Data/IConnectionFactory.cs
using System.Data;
using System.Data.Common;

namespace AllWorkHRIS.Core.Data;

/// <summary>
/// Non-sensitive details identifying which database the app is connected to.
/// Never includes credentials (no username or password).
/// </summary>
public sealed record DatabaseInfo(string Provider, string Server, string Database);

public interface IConnectionFactory
{
    /// <summary>
    /// Creates and returns an open IDbConnection using the configured
    /// database provider and connection string.
    /// Caller is responsible for disposing the connection.
    /// </summary>
    IDbConnection CreateConnection();

    /// <summary>
    /// Returns the provider, server/host, and database name from the configured
    /// connection string — for display (e.g. System Settings). Credential fields
    /// are never read or returned. Does not open a connection.
    /// </summary>
    DatabaseInfo GetDatabaseInfo();
}

public sealed class ConnectionFactory : IConnectionFactory
{
    private readonly string _connectionString;
    private readonly string _provider;

    public ConnectionFactory()
    {
        _connectionString = Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING")
            ?? throw new InvalidOperationException("DATABASE_CONNECTION_STRING not set.");
        _provider = Environment.GetEnvironmentVariable("DATABASE_PROVIDER")
            ?? throw new InvalidOperationException("DATABASE_PROVIDER not set.");
    }

    public IDbConnection CreateConnection()
    {
        IDbConnection connection = _provider.ToLowerInvariant() switch
        {
            "postgresql"  => new Npgsql.NpgsqlConnection(_connectionString),
            "sqlserver"   => new Microsoft.Data.SqlClient.SqlConnection(_connectionString),
            "mysql"       => new MySql.Data.MySqlClient.MySqlConnection(_connectionString),
            _ => throw new InvalidOperationException($"Unsupported database provider: {_provider}")
        };

        connection.Open();
        return connection;
    }

    public DatabaseInfo GetDatabaseInfo()
    {
        // Parse key/value pairs without opening a connection. Credential keys
        // (Username/User Id/Password/Pwd) are deliberately never read.
        var b = new DbConnectionStringBuilder { ConnectionString = _connectionString };

        string Lookup(params string[] keys)
        {
            foreach (var k in keys)
                if (b.TryGetValue(k, out var v) && v?.ToString() is { Length: > 0 } s)
                    return s;
            return "—";
        }

        var server   = Lookup("Host", "Server", "Data Source", "DataSource", "Address");
        var database = Lookup("Database", "Initial Catalog");
        return new DatabaseInfo(_provider, server, database);
    }
}
