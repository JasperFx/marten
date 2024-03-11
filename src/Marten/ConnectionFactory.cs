#nullable enable
using System;
using Npgsql;
using Weasel.Postgresql.Connections;

namespace Marten;

/// <summary>
///     Default, simple implementation of IConnectionFactory
/// </summary>
[Obsolete("This will be removed in Marten 8. Prefer NpgsqlDataSource instead")]
public class ConnectionFactory: IConnectionFactory
{
    private readonly INpgsqlDataSourceFactory _npgsqlDataSourceFactory;
    private readonly Lazy<string> _connectionString;

    /// <summary>
    ///     Supply a lambda that can resolve the connection string
    ///     for a Postgresql database
    /// </summary>
    /// <param name="connectionSource"></param>
    public ConnectionFactory(INpgsqlDataSourceFactory npgsqlDataSourceFactory, Func<string> connectionSource)
    {
        _npgsqlDataSourceFactory = npgsqlDataSourceFactory;
        _connectionString = new Lazy<string>(connectionSource);
    }

    /// <summary>
    ///     Supply the connection string to the Postgresql database directly
    /// </summary>
    /// <param name="connectionString"></param>
    public ConnectionFactory(INpgsqlDataSourceFactory npgsqlDataSourceFactory, string connectionString)
    {
        _npgsqlDataSourceFactory = npgsqlDataSourceFactory;
        _connectionString = new Lazy<string>(() => connectionString);
    }

    public NpgsqlConnection Create()
    {
        var dataSource = _npgsqlDataSourceFactory.Create(_connectionString.Value);
        return dataSource.CreateConnection();
    }
}
