using System;
using Npgsql;

namespace Marten.Schema
{
    /// <summary>
    /// Specify options that are passed to CREATE DATABASE.
    /// <see href="https://www.postgresql.org/docs/current/static/sql-createdatabase.html">CREATE DATABASE</see> documentation for options.
    /// </summary>
    public interface ITenantDatabaseCreationExpressions
    {
        /// <summary>
        /// If database exists, it is dropped prior to re-creation.
        /// </summary>
        /// <param name="killConnections">Kill connections to database prior to drop</param>
        /// <remarks>Requires CREATEDB privilege</remarks>
        ITenantDatabaseCreationExpressions DropExisting(bool killConnections = false);
        ITenantDatabaseCreationExpressions WithEncoding(string encoding);
        ITenantDatabaseCreationExpressions WithOwner(string owner);
        ITenantDatabaseCreationExpressions ConnectionLimit(int limit);
        ITenantDatabaseCreationExpressions LcCollate(string lcCollate);
        ITenantDatabaseCreationExpressions LcType(string lcType);
        ITenantDatabaseCreationExpressions TableSpace(string tableSpace);
        /// <summary>
        /// Check for database existence from pg_database, otherwise detect "INVALID CATALOG NAME" on connect
        /// </summary>
        /// <returns></returns>
        ITenantDatabaseCreationExpressions CheckAgainstPgDatabase();
        /// <summary>
        /// Callback to be invoked after database creation
        /// </summary>
        ITenantDatabaseCreationExpressions OnDatabaseCreated(Action<NpgsqlConnection> onDbCreated);
        /// <summary>
        /// Create PLV8 extension for database
        /// </summary>
        ITenantDatabaseCreationExpressions CreatePLV8();
    }
}