#nullable enable
using System.Threading.Tasks;
using Npgsql;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;

namespace Marten.Testing.Harness
{
    /// <summary>
    /// Provisions per-tenant databases on the test PostgreSQL server for
    /// database-per-tenant multi-tenancy tests. Databases are created once and
    /// reused across runs (creation is expensive); callers are responsible for
    /// resetting schemas or data inside them.
    /// </summary>
    public static class TenantDatabases
    {
        /// <summary>
        /// Ensure the named database exists on the server behind
        /// <paramref name="conn"/> (an open connection to the master test
        /// database) and return a connection string targeting it.
        /// </summary>
        public static async Task<string> CreateIfNotExistsAsync(NpgsqlConnection conn, string databaseName)
        {
            if (!await conn.DatabaseExists(databaseName))
            {
                try
                {
                    await new DatabaseSpecification().BuildDatabase(conn, databaseName);
                }
                catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.DuplicateDatabase
                                                      or PostgresErrorCodes.UniqueViolation)
                {
                    // The test assemblies can run once per target framework, concurrently;
                    // the check-then-create above is not atomic, so the loser of that race
                    // sees the database it wanted already there.
                }
            }

            return ConnectionStringFor(databaseName);
        }

        /// <summary>
        /// A connection string for the named database on the test server.
        /// </summary>
        public static string ConnectionStringFor(string databaseName)
        {
            return new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
            {
                Database = databaseName
            }.ConnectionString;
        }
    }
}
