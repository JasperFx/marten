using System;
using Npgsql;

namespace Marten.Schema
{
    public interface ITenantDatabaseCreationExpressions
    {
        ITenantDatabaseCreationExpressions DropExisting();
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
        ITenantDatabaseCreationExpressions OnDatabaseCreated(Action<NpgsqlConnection> onDbCreated);
    }
}