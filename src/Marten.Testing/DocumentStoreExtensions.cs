using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql.Tables;

namespace Marten.Testing;

public static class DocumentStoreExtensions
{
    /// <summary>
    /// Strictly a testing helper to guarantee that the database storage for
    /// the specified storage type is initialized in the default tenant database
    /// for this store
    /// </summary>
    /// <param name="store"></param>
    /// <param name="storageType"></param>
    /// <returns></returns>
    public static ValueTask EnsureStorageExistsAsync(this DocumentStore store, Type storageType, CancellationToken token = default) => store.Tenancy.Default.Database.EnsureStorageExistsAsync(storageType, token);

    /// <summary>
    /// Strictly a testing helper to guarantee that the database storage for
    /// the specified storage type is initialized in the default tenant database
    /// for this store
    /// </summary>
    /// <param name="store"></param>
    /// <param name="storageType"></param>
    /// <returns></returns>
    public static void EnsureStorageExists(this DocumentStore store, Type storageType) => store.Tenancy.Default.Database.EnsureStorageExists(storageType);

    /// <summary>
    /// Extension method for testing to examine the existing table in the default tenant database
    /// for the storage type
    /// </summary>
    /// <param name="store"></param>
    /// <param name="storageType"></param>
    /// <returns></returns>
    public static Task<Table> ExistingTableFor(this DocumentStore store, Type storageType)
    {
        return store.Tenancy.Default.Database.ExistingTableFor(storageType);
    }

    /// <summary>
    /// Fetch a list of the existing tables in the database
    /// </summary>
    /// <param name="database"></param>
    /// <returns></returns>
    public static Task<IReadOnlyList<DbObjectName>> SchemaTables(this DocumentStore store)
    {
        return store.Tenancy.Default.Database.SchemaTables();
    }

    public static NpgsqlConnection CreateConnection(this DocumentStore store)
    {
        return store.Tenancy.Default.Database.CreateConnection();
    }

}
