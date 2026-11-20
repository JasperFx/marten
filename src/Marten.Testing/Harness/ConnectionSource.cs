using System;
using Npgsql;
using Weasel.Postgresql.Connections;

namespace Marten.Testing.Harness;

public class ConnectionSource
{
    /// <summary>
    ///     The prefix on every whole database the 9.0 suites create or assume by a fixed name (#6109). The 9.0
    ///     and master (10.0) suites run against one Postgres server, and Marten 10 leaves partitioned event
    ///     tables that Marten 9 cannot migrate (0A000). With its own names, a 9.0 run never opens a database
    ///     master's suites made, and master never opens one of 9.0's. Name a database through
    ///     <see cref="DatabaseName" /> or <see cref="ConnectionStringForDatabase" />, never with a literal.
    /// </summary>
    public const string DatabasePrefix = "marten9_";

    /// <summary>The default database the suites run in when <c>marten_testing_database</c> is not set.</summary>
    public const string DefaultDatabase = DatabasePrefix + "testing";

    // ;Persist Security Info=true

    // Keep the default timeout pretty short
    public static readonly string ConnectionString = Environment.GetEnvironmentVariable("marten_testing_database")
                                                     ??
                                                     $"Host=localhost;Port=5432;Database={DefaultDatabase};Username=postgres;password=postgres;Command Timeout=5";

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

        ensureDefaultDatabase();
    }

    /// <summary>
    ///     The physical name of a database a test creates or uses by a fixed name: <paramref name="name" /> with
    ///     <see cref="DatabasePrefix" /> in front, once. Keep the unprefixed name as the tenant id or database
    ///     identifier the test asserts on; only the database on the server carries the prefix.
    /// </summary>
    public static string DatabaseName(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("A database name is required", nameof(name));

        return name.StartsWith(DatabasePrefix, StringComparison.Ordinal) ? name : DatabasePrefix + name;
    }

    /// <summary>
    ///     <see cref="ConnectionString" /> pointed at <see cref="DatabaseName" /> of <paramref name="name" />.
    /// </summary>
    public static string ConnectionStringForDatabase(string name)
    {
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = DatabaseName(name) }.ConnectionString;
    }

    /// <summary>
    ///     A run with no <c>marten_testing_database</c> uses <see cref="DefaultDatabase" />, which a fresh
    ///     docker-compose container does not have (it creates marten_testing). Create it from the maintenance
    ///     database. Anything else that goes wrong here surfaces in the first test that opens a connection, with
    ///     a better message than a type initializer would give.
    /// </summary>
    private static void ensureDefaultDatabase()
    {
        if (Environment.GetEnvironmentVariable("marten_testing_database") is { Length: > 0 }) return;

        try
        {
            var admin = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = "postgres", Pooling = false };
            using var conn = new NpgsqlConnection(admin.ConnectionString);
            conn.Open();

            using var check = conn.CreateCommand();
            check.CommandText = "select 1 from pg_database where datname = @name";
            check.Parameters.AddWithValue("name", DefaultDatabase);
            if (check.ExecuteScalar() is not null) return;

            using var create = conn.CreateCommand();
            create.CommandText = $"create database \"{DefaultDatabase}\"";
            create.ExecuteNonQuery();
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.DuplicateDatabase or PostgresErrorCodes.UniqueViolation)
        {
            // Two test hosts (one per target framework) raced to create it.
        }
        catch (Exception)
        {
            // Server down or no rights: the tests report it when they connect.
        }
    }
}
