using System;
using Npgsql;
using Weasel.Postgresql.Connections;

namespace Marten.Testing.Harness;

public class ConnectionSource
{
    // ;Persist Security Info=true

    // Keep the default timeout pretty short
    public static readonly string ConnectionString = Environment.GetEnvironmentVariable("marten_testing_database")
                                                     ??
                                                     "Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres;Command Timeout=5";

    /// <summary>
    ///     <see cref="ConnectionString" /> with a command timeout long enough for database administration:
    ///     <c>CREATE DATABASE</c>, <c>DROP DATABASE ... WITH (FORCE)</c> and terminating a database's sessions.
    ///     The short timeout on <see cref="ConnectionString" /> is for the tests' own queries. On a busy server,
    ///     creating or dropping a whole database takes longer than 5 seconds (#6023). Use this only for the
    ///     connection that creates or drops databases, never for a store under test.
    /// </summary>
    public static readonly string AdminConnectionString =
        new NpgsqlConnectionStringBuilder(ConnectionString) { CommandTimeout = 60 }.ConnectionString;

    /// <summary>
    ///     <see cref="ConnectionString" /> with a 60 second command timeout, for the stores the shared compliance
    ///     suites build (#6101). Their facts check behaviour, not speed: they race sessions that queue on one
    ///     lock (ten DCB allocators on one tag), and they rebuild and clean schema between facts. On a busy box
    ///     those statements take longer than 5 seconds, and Npgsql reports a command timeout as
    ///     "Exception while reading from stream" with an inner <see cref="TimeoutException" />, which reads like
    ///     a broken connection rather than a slow one.
    /// </summary>
    public static readonly string ComplianceConnectionString =
        new NpgsqlConnectionStringBuilder(ConnectionString) { CommandTimeout = 60 }.ConnectionString;

    static ConnectionSource()
    {
        if (ConnectionString.IsEmpty())
            throw new Exception(
                "You need to set the connection string for your local Postgresql database in the environment variable 'marten_testing_database'");
    }

}
