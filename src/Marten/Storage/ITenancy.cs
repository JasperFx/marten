using System;
using System.Threading.Tasks;
using JasperFx.Descriptors;
using JasperFx.MultiTenancy;
using Marten.Exceptions;
using Marten.Schema;
using Weasel.Core.Migrations;
using Weasel.Core.MultiTenancy;
using Weasel.Postgresql;

namespace Marten.Storage;

/// <summary>
/// Marks a tenancy model as having a master database
/// </summary>
public interface ITenancyWithMasterDatabase
{
    PostgresqlDatabase TenantDatabase { get; }
}

#region sample_itenancy

/// <summary>
///     Pluggable interface for Marten multi-tenancy by database
/// </summary>
public interface ITenancy: IDatabaseSource, IDisposable, IDatabaseUser
{
    /// <summary>
    ///     The default tenant. This can be null.
    /// </summary>
    Tenant Default { get; }

    /// <summary>
    ///     A composite document cleaner for the entire collection of databases
    /// </summary>
    IDocumentCleaner Cleaner { get; }

    /// <summary>
    ///     Retrieve or create a Tenant for the tenant id.
    /// </summary>
    /// <param name="tenantId"></param>
    /// <exception cref="UnknownTenantIdException"></exception>
    /// <returns></returns>
    Tenant GetTenant(string tenantId);

    /// <summary>
    ///     Retrieve or create a tenant for the tenant id
    /// </summary>
    /// <param name="tenantId"></param>
    /// <returns></returns>
    ValueTask<Tenant> GetTenantAsync(string tenantId);

    /// <summary>
    ///     Find or create the named database
    /// </summary>
    /// <param name="tenantIdOrDatabaseIdentifier"></param>
    /// <returns></returns>
    ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier);

    /// <summary>
    ///     Find or create the named database
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    ValueTask<IMartenDatabase> FindDatabase(DatabaseId id)
    {
        throw new NotImplementedException("You will need to implement this interface method to use a Marten store with Wolverine projection/subscription distribution");
    }

    /// <summary>
    ///     Find the named database WITHOUT creating or provisioning anything, returning null when the
    ///     tenant id or database identifier resolves to nothing. This is the read-only counterpart to
    ///     <see cref="FindOrCreateDatabase"/>, for diagnostics and monitoring reads that must not change
    ///     what exists just by being called.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     #5400. <see cref="FindOrCreateDatabase"/> is documented as "find or create" and several tenancy
    ///     models take the second half literally: sharded tenancy assigns an unknown tenant to a shard and
    ///     runs partition + sequence DDL for it, and single-server tenancy creates a whole PostgreSQL
    ///     database named after the id. That is correct for real tenant traffic and wrong for an explorer
    ///     read, where a typo'd or retired tenant id would silently bring a tenant into existence.
    ///     </para>
    ///     <para>
    ///     The default implementation delegates to <see cref="FindOrCreateDatabase"/> so an existing custom
    ///     tenancy keeps working exactly as before rather than breaking on an added member. That default is
    ///     deliberately the permissive one: a tenancy that provisions will still provision until it
    ///     overrides this, which is no worse than today. Marten's own tenancy models all override it.
    ///     </para>
    /// </remarks>
    /// <param name="tenantIdOrDatabaseIdentifier"></param>
    /// <returns>The database, or null when nothing is registered under that identifier.</returns>
    async ValueTask<IMartenDatabase?> TryFindDatabase(string tenantIdOrDatabaseIdentifier)
        => await FindOrCreateDatabase(tenantIdOrDatabaseIdentifier).ConfigureAwait(false);

    /// <summary>
    ///  Asserts that the requested tenant id is part of the current database
    /// </summary>
    /// <param name="database"></param>
    /// <param name="tenantId"></param>
    bool IsTenantStoredInCurrentDatabase(IMartenDatabase database, string tenantId);
}

#endregion
