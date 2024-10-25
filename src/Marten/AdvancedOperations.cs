#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Projections;
using Marten.Events.Protected;
using Marten.Events.TestSupport;
using Marten.Schema;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables.Partitioning;

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
    /// Advance the high water mark to the latest detected sequence. Use with caution!
    /// This is mostly meant for teams that retrofit asynchronous projections to a
    /// very large event store that has never before used projections. This will help
    /// the daemon start and function in its "catch up" mode
    /// </summary>
    public async Task AdvanceHighWaterMarkToLatestAsync(CancellationToken token)
    {
        var databases = await _store.Tenancy.BuildDatabases().ConfigureAwait(false);
        foreach (var database in databases.OfType<MartenDatabase>())
        {
            var detector = new HighWaterDetector(database, _store.Events, NullLogger.Instance);
            await detector.AdvanceHighWaterMarkToLatest(token).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Mostly for testing support. Register a new IInitialData object
    ///     that would be called from ResetAllData() later.
    /// </summary>
    public List<IInitialData> InitialDataCollection => _store.Options.InitialData;

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
        tenantId = _store.Options.MaybeCorrectTenantId(tenantId);
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
            : (await _store.Tenancy.GetTenantAsync(_store.Options.MaybeCorrectTenantId(tenantId)).ConfigureAwait(false)).Database;

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
            : (await _store.Tenancy.GetTenantAsync(_store.Options.MaybeCorrectTenantId(tenantId)).ConfigureAwait(false)).Database;

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
            : await _store.Tenancy.GetTenantAsync(_store.Options.MaybeCorrectTenantId(tenantId)).ConfigureAwait(false);
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
    /// Convenience method to retrieve all valid "ShardName" identities of asynchronous projections
    /// </summary>
    /// <returns></returns>
    public IReadOnlyList<ShardName> AllAsyncProjectionShardNames()
    {
        return _store
            .Options
            .Projections
            .All
            .Where(x => x.Lifecycle == ProjectionLifecycle.Async)
            .SelectMany(x => x.AsyncProjectionShards(_store))
            .Select(x => x.Name)
            .ToList();
    }

    /// <summary>
    /// Convenience method to rebuild the projected document of type T for a single stream
    /// identified by id
    /// *You will still have to call SaveChangesAsync() to commit the changes though!*
    /// </summary>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public async Task RebuildSingleStreamAsync<T>(string id, CancellationToken token = default) where T : class
    {
        await using var session = _store.LightweightSession();
        var document = await session.Events.AggregateStreamAsync<T>(id, token:token).ConfigureAwait(false);
        session.Store(document);
        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// Convenience method to rebuild the projected document of type T for a single stream
    /// identified by id
    /// *You will still have to call SaveChangesAsync() to commit the changes though!*
    /// </summary>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public async Task RebuildSingleStreamAsync<T>(Guid id, CancellationToken token = default) where T : class
    {
        await using var session = _store.LightweightSession();
        var document = await session.Events.AggregateStreamAsync<T>(id, token:token).ConfigureAwait(false);
        session.Store(document);
        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// "Upsert" tenant ids and matching partition suffixes to all conjoined, multi-tenanted
    /// tables *if* Marten-managed partitioning is applied to this store. This assumes a 1-1
    /// relationship between tenant ids and table partitions
    /// </summary>
    /// <param name="token"></param>
    /// <param name="tenantIds"></param>
    public Task AddMartenManagedTenantsAsync(CancellationToken token, params string[] tenantIds)
    {
        var dict = new Dictionary<string, string>();
        foreach (var tenantId in tenantIds)
        {
            dict[tenantId] = tenantId;
        }

        return AddMartenManagedTenantsAsync(token, dict);
    }

    /// <summary>
    /// "Upsert" tenant ids and matching partition suffixes to all conjoined, multi-tenanted
    /// tables *if* Marten-managed partitioning is applied to this store. This assumes a 1-1
    /// relationship between tenant ids and table partitions
    /// </summary>
    /// <param name="token"></param>
    /// <param name="tenantIdToPartitionMapping">Dictionary of tenant id to partition names</param>
    public async Task<TablePartitionStatus[]> AddMartenManagedTenantsAsync(CancellationToken token, Dictionary<string, string> tenantIdToPartitionMapping)
    {
        if (_store.Options.TenantPartitions == null)
        {
            throw new InvalidOperationException(
                $"Marten-managed per-tenant partitioning is not active in this store. Did you miss a call to {nameof(StoreOptions)}.{nameof(StoreOptions.Policies)}.{nameof(StoreOptions.PoliciesExpression.PartitionMultiTenantedDocumentsUsingMartenManagement)}()?");
        }

        if (_store.Tenancy is not DefaultTenancy)
            throw new InvalidOperationException(
                "This option is not (yet) supported in combination with database per tenant multi-tenancy");
        var database = (PostgresqlDatabase)_store.Tenancy.Default.Database;


        var logger = _store.Options.LogFactory?.CreateLogger<DocumentStore>() ?? NullLogger<DocumentStore>.Instance;
        return await _store.Options.TenantPartitions.Partitions.AddPartitionToAllTables(
            logger,
            database,
            tenantIdToPartitionMapping,
            token).ConfigureAwait(false);
    }

    /// <summary>
    /// Configure and execute a batch masking of protected data for a subset of the events
    /// in the event store
    /// </summary>
    /// <returns></returns>
    public Task ApplyEventDataMasking(Action<IEventDataMasking> configure, CancellationToken token = default)
    {
        var masking = new EventDataMasking(_store);
        configure(masking);
        return masking.ApplyAsync(token);
    }
}
