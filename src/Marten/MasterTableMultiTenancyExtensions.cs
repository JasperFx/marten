using System;
using System.Threading.Tasks;
using Marten.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Marten;

public static class MasterTableMultiTenancyExtensions
{
    /// <summary>
    /// Convenience method to clear all tenant database records
    /// if using Marten
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static Task ClearAllTenantDatabaseRecordsAsync(this IHost host)
    {
        var store = host.Services.GetRequiredService<IDocumentStore>() as DocumentStore;
        var tenancy = store?.Options.Tenancy as MasterTableTenancy;
        if (tenancy is null)
            throw new InvalidOperationException("The Marten tenancy model is not using the master table tenancy");

        return tenancy.ClearAllDatabaseRecordsAsync();

    }

    /// <summary>
    /// Convenience method to add a new tenant database to the master tenant table at runtime
    /// </summary>
    /// <param name="host"></param>
    /// <param name="tenantId"></param>
    /// <param name="connectionString"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static Task AddTenantDatabaseAsync(this IHost host, string tenantId, string connectionString)
    {
        var store = host.Services.GetRequiredService<IDocumentStore>() as DocumentStore;
        var tenancy = store?.Options.Tenancy as MasterTableTenancy;
        if (tenancy is null)
            throw new InvalidOperationException("The Marten tenancy model is not using the master table tenancy");

        return tenancy.AddDatabaseRecordAsync(tenantId, connectionString);
    }
}
