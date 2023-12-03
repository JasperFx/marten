#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.TestSupport;
using Marten.Schema;
using Marten.Storage;

namespace Marten;

/// <summary>
///     Access to advanced, rarely used features of IDocumentStore
/// </summary>
public class AdvancedOperations
{
    private readonly DocumentStore _store;

    internal AdvancedOperations(DocumentStore store)
    {
        _store = store;
    }

    /// <summary>
    ///     Used to remove document data and tables from the current Postgresql database
    /// </summary>
    public IDocumentCleaner Clean => _store.Tenancy.Cleaner;

    public ISerializer Serializer => _store.Serializer;

    /// <summary>
    ///     Mostly for testing support. Register a new IInitialData object
    ///     that would be called from ResetAllData() later.
    /// </summary>
    public IList<IInitialData> InitialDataCollection => _store.Options.InitialData;

    /// <summary>
    ///     Deletes all current document and event data, then (re)applies the configured
    ///     initial data
    /// </summary>
    public async Task ResetAllData(CancellationToken cancellation = default)
    {
        var databases = await _store.Tenancy.BuildDatabases().ConfigureAwait(false);
        foreach (var database in databases.OfType<IMartenDatabase>())
        {
            await database.DeleteAllDocumentsAsync(cancellation).ConfigureAwait(false);
            await database.DeleteAllEventDataAsync(cancellation).ConfigureAwait(false);
        }


        foreach (var initialData in _store.Options.InitialData)
            await initialData.Populate(_store, cancellation).ConfigureAwait(false);
    }

    /// <summary>
    ///     Set the minimum sequence number for a Hilo sequence for a specific document type
    ///     to the specified floor. Useful for migrating data between databases
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="floor"></param>
    /// <param name="tenantId">If supplied, this will only apply to the database holding the named tenantId</para>
    public async Task ResetHiloSequenceFloor<T>(long floor)
    {
        var databases = await _store.Tenancy.BuildDatabases().ConfigureAwait(false);
        foreach (var database in databases.OfType<IMartenDatabase>())
            await database.ResetHiloSequenceFloor<T>(floor).ConfigureAwait(false);
    }

    /// <summary>
    ///     Set the minimum sequence number for a Hilo sequence for a specific document type
    ///     to the specified floor. Useful for migrating data between databases
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="floor"></param>
    public async Task ResetHiloSequenceFloor<T>(string tenantId, long floor)
    {
        var tenant = await _store.Tenancy.GetTenantAsync(tenantId).ConfigureAwait(false);
        await tenant.Database.ResetHiloSequenceFloor<T>(floor).ConfigureAwait(false);
    }

    /// <summary>
    ///     Fetch the current size of the event store tables, including the current value
    ///     of the event sequence number
    /// </summary>
    /// <param name="tenantId">
    ///     Specify the database containing this tenant id. If omitted, this method uses the default
    ///     database
    /// </param>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<EventStoreStatistics> FetchEventStoreStatistics(string? tenantId = null,
        CancellationToken token = default)
    {
        var database = tenantId == null
            ? _store.Tenancy.Default.Database
            : (await _store.Tenancy.GetTenantAsync(tenantId).ConfigureAwait(false)).Database;

        return await database.FetchEventStoreStatistics(token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Check the current progress of all asynchronous projections
    /// </summary>
    /// <param name="token"></param>
    /// <param name="tenantId">
    ///     Specify the database containing this tenant id. If omitted, this method uses the default
    ///     database
    /// </param>
    /// <returns></returns>
    public async Task<IReadOnlyList<ShardState>> AllProjectionProgress(string? tenantId = null,
        CancellationToken token = default)
    {
        var database = tenantId == null
            ? _store.Tenancy.Default.Database
            : (await _store.Tenancy.GetTenantAsync(tenantId).ConfigureAwait(false)).Database;

        return await database.AllProjectionProgress(token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Check the current progress of a single projection or projection shard
    /// </summary>
    /// <param name="tenantId">
    ///     Specify the database containing this tenant id. If omitted, this method uses the default
    ///     database
    /// </param>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<long> ProjectionProgressFor(ShardName name, string? tenantId = null,
        CancellationToken token = default)
    {
        var tenant = tenantId == null
            ? _store.Tenancy.Default
            : await _store.Tenancy.GetTenantAsync(tenantId).ConfigureAwait(false);
        var database = tenant.Database;

        return await database.ProjectionProgressFor(name, token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Marten's built in test support for event projections. Only use this in testing as
    ///     it will delete existing event and projected aggregate data
    /// </summary>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public Task EventProjectionScenario(Action<ProjectionScenario> configuration, CancellationToken ct = default)
    {
        var scenario = new ProjectionScenario(_store);
        configuration(scenario);

        return scenario.Execute(ct);
    }

    /// <summary>
    /// Reload types to flush Npgsql cache for a tenant
    /// </summary>
    /// <param name="tenantId"></param>
    /// <typeparam name="T"></typeparam>
    public async Task ReloadTypes(string tenantId)
    {
        var tenant = await _store.Tenancy.GetTenantAsync(tenantId).ConfigureAwait(false);
        await tenant.Database.ReloadTypesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Reload types to flush Npgsql cache across all databases
    /// </summary>
    /// <param name="floor"></param>
    /// <typeparam name="T"></typeparam>
    public async Task ReloadTypes()
    {
        var databases = await _store.Tenancy.BuildDatabases().ConfigureAwait(false);
        foreach (var database in databases.OfType<IMartenDatabase>())
            await database.ReloadTypesAsync().ConfigureAwait(false);
    }
}
