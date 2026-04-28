// AllWorkHRIS.Core/Data/IConnectionFactory.cs
using System.Data;

namespace AllWorkHRIS.Core.Data;

public interface IConnectionFactory
{
    /// <summary>
    /// Creates and returns an open IDbConnection using the configured
    /// database provider and connection string.
    /// Caller is responsible for disposing the connection.
    /// </summary>
    IDbConnection CreateConnection();
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
}
