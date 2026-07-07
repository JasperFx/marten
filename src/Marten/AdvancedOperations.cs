using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten.Events;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Protected;
using Marten.Events.TestSupport;
using Marten.Internal;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
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
    ///     Mostly for testing support. Register a new IInitialData object
    ///     that would be called from ResetAllData() later.
    /// </summary>
    public List<IInitialData> InitialDataCollection => _store.Options.InitialData;

    /// <summary>
    ///     Advance the high water mark to the latest detected sequence. Use with caution!
    ///     This is mostly meant for teams that retrofit asynchronous projections to a
    ///     very large event store that has never before used projections. This will help
    ///     the daemon start and function in its "catch up" mode
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
    ///     Advance the high water mark to the latest detected sequence. Use with caution!
    ///     This is mostly meant for teams that retrofit asynchronous projections to a
    ///     very large event store that has never before used projections. This will help
    ///     the daemon start and function in its "catch up" mode
    /// </summary>
    public async Task AdvanceHighWaterMarkToLatestAsync(string tenantId, CancellationToken token)
    {
        tenantId = _store.Options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        var tenant = await _store.Tenancy.GetTenantAsync(tenantId).ConfigureAwait(false);

        var detector = new HighWaterDetector((MartenDatabase)tenant.Database, _store.Events, NullLogger.Instance);
        await detector.AdvanceHighWaterMarkToLatest(token).ConfigureAwait(false);
    }


    /// <summary>
    ///     If the "high water mark" and event progression values somehow advance beyond the highest
    ///     event sequence, this resets the values back to the highest sequential number. This is very unlikely
    ///     to occur *now*, but there was a scenario where a Marten application connected to a PostgreSQL database
    ///     that was being shut down could see inconsistent data from PostgreSQL. We believe this has been addressed
    ///     now in Marten internals, but this method exists "just in case"
    /// </summary>
    /// <param name="cancellationToken"></param>
    public async Task TryCorrectProgressInDatabaseAsync(CancellationToken cancellationToken)
    {
        var databases = await _store.Tenancy.BuildDatabases().ConfigureAwait(false);
        foreach (var database in databases)
        {
            var detector = new HighWaterDetector((MartenDatabase)database, _store.Events, NullLogger.Instance);
            await detector.TryCorrectProgressInDatabaseAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     If the "high water mark" and event progression values somehow advance beyond the highest
    ///     event sequence, this resets the values back to the highest sequential number. This is very unlikely
    ///     to occur *now*, but there was a scenario where a Marten application connected to a PostgreSQL database
    ///     that was being shut down could see inconsistent data from PostgreSQL. We believe this has been addressed
    ///     now in Marten internals, but this method exists "just in case"
    /// </summary>
    /// <param name="cancellationToken"></param>
    public async Task TryCorrectProgressInDatabaseAsync(string tenantId, CancellationToken cancellationToken)
    {
        tenantId = _store.Options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        var tenant = await _store.Tenancy.GetTenantAsync(tenantId).ConfigureAwait(false);
        var detector = new HighWaterDetector((MartenDatabase)tenant.Database, _store.Events, NullLogger.Instance);
        await detector.TryCorrectProgressInDatabaseAsync(cancellationToken).ConfigureAwait(false);
    }

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
        tenantId = _store.Options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
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
            : (await _store.Tenancy.GetTenantAsync(_store.Options.TenantIdStyle.MaybeCorrectTenantId(tenantId))
                .ConfigureAwait(false)).Database;

        return await database.FetchEventStoreStatistics(token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Check the current progress of all asynchronous projections
    /// </summary>
    /// <param name="token"></param>
    /// <param name="tenantId">
    ///     Specify the database containing this tenant id. If omitted, this method uses the default
    ///     database when the store has a single database, or spans <em>every</em> known database
    ///     under multi-tenancy with multiple databases (including
    ///     <c>MultiTenantedWithShardedDatabases</c>), concatenating each database's progression rows.
    ///     Under <c>Events.UseTenantPartitionedEvents</c> the per-tenant rows carry the tenant id in
    ///     their <c>ShardName.Identity</c> (<c>{Name}:{ShardKey}:{tenantId}</c>), so the aggregated
    ///     result remains attributable per tenant. See
    ///     <a href="https://github.com/JasperFx/marten/issues/4797">#4797</a>.
    /// </param>
    /// <returns></returns>
    public async Task<IReadOnlyList<ShardState>> AllProjectionProgress(string? tenantId = null,
        CancellationToken token = default)
    {
        if (tenantId != null)
        {
            var database =
                (await _store.Tenancy.GetTenantAsync(_store.Options.TenantIdStyle.MaybeCorrectTenantId(tenantId))
                    .ConfigureAwait(false)).Database;
            return await database.AllProjectionProgress(token).ConfigureAwait(false);
        }

        // #4797: mirror WaitForNonStaleProjectionDataAsync's sharded-aware shape (#4366).
        // A single-database store keeps today's default-database behavior; any
        // multi-database tenancy (sharded, database per tenant, master-table, ...) has no
        // usable "default" database — ShardedTenancy.Default even throws — so fan out
        // across every database the store knows about and concatenate the results.
        if (_store.Tenancy is DefaultTenancy)
        {
            return await _store.Tenancy.Default.Database.AllProjectionProgress(token).ConfigureAwait(false);
        }

        var databases = await _store.Storage.AllDatabases().ConfigureAwait(false);
        assertHasDatabases(databases);

        var states = new List<ShardState>();
        foreach (var database in databases)
        {
            states.AddRange(await database.AllProjectionProgress(token).ConfigureAwait(false));
        }

        return states;
    }

    /// <summary>
    ///     Check the current progress of a single projection or projection shard
    /// </summary>
    /// <param name="tenantId">
    ///     Specify the database containing this tenant id. If omitted, this method uses the default
    ///     database when the store has a single database. Under multi-tenancy with multiple databases
    ///     (including <c>MultiTenantedWithShardedDatabases</c>) an omitted tenant id spans
    ///     <em>every</em> known database and returns the highest progression found for the shard
    ///     name. With <c>Events.UseTenantPartitionedEvents</c>, a tenant-qualified
    ///     <c>ShardName</c> identity (<c>{Name}:{ShardKey}:{tenantId}</c>) only ever exists in the
    ///     single database that owns the tenant, so the result is that tenant's exact progression;
    ///     databases without the row contribute 0. See
    ///     <a href="https://github.com/JasperFx/marten/issues/4797">#4797</a>.
    /// </param>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<long> ProjectionProgressFor(ShardName name, string? tenantId = null,
        CancellationToken token = default)
    {
        if (tenantId != null)
        {
            var tenant = await _store.Tenancy
                .GetTenantAsync(_store.Options.TenantIdStyle.MaybeCorrectTenantId(tenantId))
                .ConfigureAwait(false);
            return await tenant.Database.ProjectionProgressFor(name, token).ConfigureAwait(false);
        }

        // #4797: same sharded-aware fan-out as AllProjectionProgress above.
        if (_store.Tenancy is DefaultTenancy)
        {
            return await _store.Tenancy.Default.Database.ProjectionProgressFor(name, token).ConfigureAwait(false);
        }

        var databases = await _store.Storage.AllDatabases().ConfigureAwait(false);
        assertHasDatabases(databases);

        var highest = 0L;
        foreach (var database in databases)
        {
            var sequence = await database.ProjectionProgressFor(name, token).ConfigureAwait(false);
            if (sequence > highest)
            {
                highest = sequence;
            }
        }

        return highest;
    }

    private static void assertHasDatabases(IReadOnlyList<IMartenDatabase> databases)
    {
        if (databases.Count == 0)
        {
            throw new InvalidOperationException(
                "The document store has no databases registered with its tenancy. Either configure a tenancy strategy that exposes databases (e.g. MultiTenantedWithShardedDatabases) or invoke a specific tenant id overload.");
        }
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
    ///     Convenience method to retrieve all valid "ShardName" identities of asynchronous projections
    /// </summary>
    /// <returns></returns>
    public IReadOnlyList<ShardName> AllAsyncProjectionShardNames()
    {
        return _store
            .Options
            .Projections
            .All
            .Where(x => x.Lifecycle == ProjectionLifecycle.Async)
            .SelectMany(x => x.Shards())
            .Select(x => x.Name)
            .ToList();
    }

    /// <summary>
    ///     Convenience method to rebuild the projected document of type T for a single stream
    ///     identified by id
    ///     *You will still have to call SaveChangesAsync() to commit the changes though!*
    /// </summary>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public async Task RebuildSingleStreamAsync<T>(string id, CancellationToken token = default) where T : class
    {
        await using var session = _store.LightweightSession();
        var document = await session.Events.AggregateStreamAsync<T>(id, token: token).ConfigureAwait(false);
        session.Store(document!);
        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Convenience method to rebuild the projected document of type T for a single stream
    ///     identified by id
    ///     *You will still have to call SaveChangesAsync() to commit the changes though!*
    /// </summary>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public async Task RebuildSingleStreamAsync<T>(Guid id, CancellationToken token = default) where T : class
    {
        await using var session = _store.LightweightSession(new SessionOptions{ConcurrencyChecks = ConcurrencyChecks.Disabled});
        var document = await session.Events.AggregateStreamAsync<T>(id, token: token).ConfigureAwait(false);
        session.Store(document!);
        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Tenant-scoped overload of <see cref="RebuildSingleStreamAsync{T}(string, CancellationToken)"/>.
    ///     Convenience method to rebuild the projected document of type T for a single stream
    ///     identified by <paramref name="streamKey"/> under the supplied <paramref name="tenantId"/>.
    ///     Required when <see cref="Marten.Events.IEventStoreOptions.TenancyStyle"/> is
    ///     <see cref="Marten.Storage.TenancyStyle.Conjoined"/> or when running under sharded
    ///     multi-tenancy — the event load and the upsert both have to be scoped to the
    ///     tenant or you get a default-tenant lookup miss / write.
    ///     *You will still have to call SaveChangesAsync() to commit the changes though!*
    ///     #4668 — new overload, not a default parameter, so existing call sites bind unchanged.
    /// </summary>
    /// <param name="streamKey">The string-keyed stream id.</param>
    /// <param name="tenantId">Tenant id to scope the rebuild session and the projected-document upsert to.</param>
    /// <param name="token"></param>
    /// <typeparam name="T"></typeparam>
    public async Task RebuildSingleStreamAsync<T>(string streamKey, string tenantId, CancellationToken token = default) where T : class
    {
        await using var session = _store.LightweightSession(tenantId);
        var document = await session.Events.AggregateStreamAsync<T>(streamKey, token: token).ConfigureAwait(false);
        session.Store(document!);
        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Tenant-scoped overload of <see cref="RebuildSingleStreamAsync{T}(Guid, CancellationToken)"/>.
    ///     Convenience method to rebuild the projected document of type T for a single stream
    ///     identified by <paramref name="streamId"/> under the supplied <paramref name="tenantId"/>.
    ///     Required when <see cref="Marten.Events.IEventStoreOptions.TenancyStyle"/> is
    ///     <see cref="Marten.Storage.TenancyStyle.Conjoined"/> or when running under sharded
    ///     multi-tenancy — the event load and the upsert both have to be scoped to the
    ///     tenant or you get a default-tenant lookup miss / write.
    ///     *You will still have to call SaveChangesAsync() to commit the changes though!*
    ///     #4668 — new overload, not a default parameter, so existing call sites bind unchanged.
    /// </summary>
    /// <param name="streamId">The Guid-keyed stream id.</param>
    /// <param name="tenantId">Tenant id to scope the rebuild session and the projected-document upsert to.</param>
    /// <param name="token"></param>
    /// <typeparam name="T"></typeparam>
    public async Task RebuildSingleStreamAsync<T>(Guid streamId, string tenantId, CancellationToken token = default) where T : class
    {
        // Mirror the Guid-overload's ConcurrencyChecks.Disabled posture so behavior matches
        // the existing non-tenanted Guid path beyond the tenant scope.
        await using var session = _store.LightweightSession(new SessionOptions
        {
            ConcurrencyChecks = ConcurrencyChecks.Disabled,
            TenantId = tenantId
        });
        var document = await session.Events.AggregateStreamAsync<T>(streamId, token: token).ConfigureAwait(false);
        session.Store(document!);
        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    ///     "Upsert" tenant ids and matching partition suffixes to all conjoined, multi-tenanted
    ///     tables *if* Marten-managed partitioning is applied to this store. This assumes a 1-1
    ///     relationship between tenant ids and table partitions
    /// </summary>
    /// <param name="token"></param>
    /// <param name="tenantIds"></param>
    public Task<TablePartitionStatus[]> AddMartenManagedTenantsAsync(CancellationToken token, params string[] tenantIds)
    {
        var dict = new Dictionary<string, string>();
        foreach (var tenantId in tenantIds) dict[tenantId] = tenantId;

        return AddMartenManagedTenantsAsync(token, dict);
    }

    /// <summary>
    ///     "Upsert" tenant ids and matching partition suffixes to all conjoined, multi-tenanted
    ///     tables *if* Marten-managed partitioning is applied to this store. The Guid tenant ids
    ///     are converted to strings via ToString() and used as both tenant id and partition suffix
    /// </summary>
    /// <param name="token"></param>
    /// <param name="tenantIds"></param>
    public Task<TablePartitionStatus[]> AddMartenManagedTenantsAsync(CancellationToken token, params Guid[] tenantIds)
    {
        // The tenant id stays the canonical Guid string, but the partition *suffix* uses the
        // hyphen-free "N" format (e.g. "538f87e5...") so it is a legal PostgreSQL identifier
        // fragment. Previously this delegated to id.ToString() (with hyphens), which always
        // failed suffix validation. See https://github.com/JasperFx/marten/issues/4567.
        return AddMartenManagedTenantsAsync(token,
            tenantIds.ToDictionary(id => id.ToString(), id => id.ToString("N")));
    }

    /// <summary>
    ///     "Upsert" tenant ids and matching partition suffixes to all conjoined, multi-tenanted
    ///     tables *if* Marten-managed partitioning is applied to this store. The Guid tenant ids
    ///     are converted to strings via ToString() as tenant ids, and the supplied function is used
    ///     to determine the partition suffix for each tenant
    /// </summary>
    /// <param name="token"></param>
    /// <param name="tenantIds"></param>
    /// <param name="partitionSuffixFromTenantId">Function to derive the partition suffix from a Guid tenant id</param>
    public Task<TablePartitionStatus[]> AddMartenManagedTenantsAsync(CancellationToken token, Guid[] tenantIds,
        Func<Guid, string> partitionSuffixFromTenantId)
    {
        var dict = new Dictionary<string, string>();
        foreach (var tenantId in tenantIds) dict[tenantId.ToString()] = partitionSuffixFromTenantId(tenantId);

        return AddMartenManagedTenantsAsync(token, dict);
    }

    /// <summary>
    ///     "Upsert" tenant ids and matching partition suffixes to all conjoined, multi-tenanted
    ///     tables *if* Marten-managed partitioning is applied to this store. This assumes a 1-1
    ///     relationship between tenant ids and table partitions
    /// </summary>
    /// <param name="token"></param>
    /// <param name="tenantIdToPartitionMapping">Dictionary of tenant id to partition names</param>
    public async Task<TablePartitionStatus[]> AddMartenManagedTenantsAsync(CancellationToken token,
        Dictionary<string, string> tenantIdToPartitionMapping)
    {
        if (_store.Options.TenantPartitions == null)
        {
            throw new InvalidOperationException(
                $"Marten-managed per-tenant partitioning is not active in this store. Did you miss a call to {nameof(StoreOptions)}.{nameof(StoreOptions.Policies)}.{nameof(StoreOptions.PoliciesExpression.PartitionMultiTenantedDocumentsUsingMartenManagement)}()?");
        }

        if (_store.Tenancy is not DefaultTenancy)
        {
            // #4598: route the sharded case to the dynamic-tenant provisioning path
            // (which covers both the document LIST partitions AND the per-tenant event
            // sequence under UseTenantPartitionedEvents). The Marten-managed partition
            // model assumes one DocumentStore-wide partition registry — under sharded
            // tenancy that registry lives per shard, so we delegate per tenant rather
            // than building a registry-wide dictionary across all shards.
            if (_store.Tenancy is ShardedTenancy sharded)
            {
                foreach (var pair in tenantIdToPartitionMapping)
                {
                    // Sharded provisioning has a 1:1 tenant↔partition-suffix shape
                    // (see ShardedTenancy.createPartitionsForTenant), so we reject any
                    // override where the caller wants a partition suffix that differs
                    // from the tenant id rather than silently dropping it.
                    if (!string.Equals(pair.Key, pair.Value, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Under sharded multi-tenancy, tenant id and partition suffix must match. " +
                            $"Got tenantId='{pair.Key}' suffix='{pair.Value}'. " +
                            $"Use AddTenantToShardAsync(tenantId) (auto-assign) or AddTenantToShardAsync(tenantId, databaseId) instead.");
                    }

                    await sharded.AddTenantAsync(pair.Key, token).ConfigureAwait(false);
                }

                // Sharded provisioning's per-shard table statuses come from each
                // shard's own AddPartitionToAllTables call (richer shape than the
                // single-DB TablePartitionStatus[] this method returns). Return empty
                // — callers wanting per-shard status should use the per-shard admin path.
                return Array.Empty<TablePartitionStatus>();
            }

            throw new InvalidOperationException(
                "AddMartenManagedTenantsAsync supports DefaultTenancy and ShardedTenancy. " +
                "MasterTableTenancy uses caller-supplied connection strings — call " +
                "IServiceProvider.AddTenantAsync(tenantId, connectionValue) instead.");
        }

        // #4648: AddGlobalProjection routes its aggregate's events to the *DEFAULT* tenant
        // slot (GlobalEventAppenderDecorator), and under UseTenantPartitionedEvents every
        // tenant id that receives events must have a registered partition — otherwise the
        // append raises MT002. The sentinel '*DEFAULT*' can never be its own partition
        // SUFFIX (it contains characters that are illegal in PG identifiers), but a LIST
        // partition VALUE can be any string — only the child table's NAME is
        // identifier-constrained. So whenever this store has global aggregates registered,
        // auto-provision the reserved '__default__' suffix for the '*DEFAULT*' partition
        // value alongside whatever tenants the caller is registering. Idempotent: the
        // partition upsert and CREATE SEQUENCE IF NOT EXISTS both tolerate re-registration.
        if (_store.Options.Events.UseTenantPartitionedEvents
            && _store.Options.EventGraph.GlobalAggregates.Any()
            && !tenantIdToPartitionMapping.ContainsKey(StorageConstants.DefaultTenantId))
        {
            var conflict = tenantIdToPartitionMapping.FirstOrDefault(pair =>
                pair.Value == MartenManagedTenantListPartitions.DefaultTenantSuffix);
            if (conflict.Key != null)
            {
                throw new ArgumentException(
                    $"The partition suffix '{MartenManagedTenantListPartitions.DefaultTenantSuffix}' is reserved " +
                    $"for the default tenant partition backing global projections (AddGlobalProjection) and cannot " +
                    $"be used for tenant '{conflict.Key}'.",
                    nameof(tenantIdToPartitionMapping));
            }

            // Copy rather than mutating the caller's dictionary
            tenantIdToPartitionMapping = new Dictionary<string, string>(tenantIdToPartitionMapping)
            {
                [StorageConstants.DefaultTenantId] = MartenManagedTenantListPartitions.DefaultTenantSuffix
            };
        }

        AssertValidPostgresqlIdentifiers(tenantIdToPartitionMapping.Values);

        var database = (PostgresqlDatabase)_store.Tenancy.Default.Database;

        AssertSuffixesWithinIdentifierLimit(tenantIdToPartitionMapping.Values, LongestPartitionedTableNameLength(database));

        var logger = _store.Options.LogFactory?.CreateLogger<DocumentStore>() ?? NullLogger<DocumentStore>.Instance;

        // #4641: under AutoCreate.None the admin call previously half-installed
        // — partition registry rows got written but mt_events / mt_streams
        // parent tables and mt_quick_append_events function were missing,
        // because the regular ensureStorageExistsAsync path is gated off under
        // None. The next append then failed with 42P01 / 42883.
        //
        // Permissive AutoCreate values (All / CreateOrUpdate / CreateOnly)
        // already lazy-install the events feature on the first session's
        // SaveChangesAsync, so this bypass only needs to fire for None. Apply
        // the events feature once here with a scoped CreateOrUpdate override
        // to respect the user's explicit "yes, mutate schema" intent of the
        // admin call (already exempt for partition-table + per-tenant-sequence
        // DDL — extend the same exemption to the surrounding events feature).
        //
        // CreateOnly intentionally NOT bypassed: doing so trips Weasel's
        // Function body-diff on a subsequent ensureStorageExistsAsync (which
        // CreateOnly forbids — only Create deltas are allowed). CreateOnly
        // already worked via the lazy path before the fix, so the scoped
        // override is sufficient for the only mode that was broken.
        if (_store.Options.Events.UseTenantPartitionedEvents
            && _store.Options.AutoCreateSchemaObjects == JasperFx.AutoCreate.None)
        {
            var migration = await database.CreateMigrationAsync(typeof(IEvent), token).ConfigureAwait(false);
            if (migration.Difference != Weasel.Core.SchemaPatchDifference.None)
            {
                await using var ddlConn = database.CreateConnection();
                await ddlConn.OpenAsync(token).ConfigureAwait(false);
                try
                {
                    await _store.Options.Advanced.Migrator
                        .ApplyAllAsync(ddlConn, migration, JasperFx.AutoCreate.CreateOrUpdate, ct: token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    await ddlConn.CloseAsync().ConfigureAwait(false);
                }
            }
        }

        var statuses = await _store.Options.TenantPartitions.Partitions.AddPartitionToAllTables(
            logger,
            database,
            tenantIdToPartitionMapping,
            token).ConfigureAwait(false);

        // #4596 Phase 1 Session 2: when UseTenantPartitionedEvents is on, also
        // create the per-tenant event sequence `mt_events_sequence_{suffix}` for
        // every freshly-registered partition. Without this, the
        // QuickAppendEventFunction's `nextval(mt_events_sequence_<suffix>)` would
        // fail for tenants that joined post-schema-apply.
        // #4598: extracted into PerTenantEventSequences.EnsureSequencesAsync so the
        // sharded runtime-assignment path (ShardedTenancy.createPartitionsForTenant)
        // can call the same implementation against the assigned shard database.
        if (_store.Options.Events.UseTenantPartitionedEvents)
        {
            await Events.Schema.PerTenantEventSequences.EnsureSequencesAsync(
                database,
                _store.Options.Events.DatabaseSchemaName,
                tenantIdToPartitionMapping.Values,
                token).ConfigureAwait(false);
        }

        return statuses;
    }

    // Partition suffixes are always concatenated onto an already-valid table name prefix
    // (e.g. "mt_doc_mymessage_"), so the suffix itself does NOT need to satisfy the
    // "identifiers start with a letter or underscore" rule — a digit-leading suffix such as
    // a sanitized Guid ("538f87e5_...") produces a perfectly valid full identifier. We only
    // reject characters that are illegal inside an unquoted identifier (anything other than
    // letters, digits, and underscores). See https://github.com/JasperFx/marten/issues/4567.
    internal static readonly Regex ValidPostgresqlIdentifierRegex = new(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    internal static void AssertValidPostgresqlIdentifiers(IEnumerable<string> suffixes)
    {
        var invalidSuffixes = suffixes.Where(s => !ValidPostgresqlIdentifierRegex.IsMatch(s)).ToArray();
        if (invalidSuffixes.Length > 0)
        {
            throw new ArgumentException(
                $"The following partition suffix values contain illegal characters for PostgreSQL object identifiers: {string.Join(", ", invalidSuffixes.Select(s => $"'{s}'"))}. Suffixes may contain only letters, digits, and underscores.");
        }
    }

    /// <summary>
    /// The maximum length of a PostgreSQL object identifier in bytes (NAMEDATALEN - 1). Identifiers
    /// longer than this are silently truncated by Postgres, which can collide partition tables.
    /// </summary>
    internal const int PostgresqlIdentifierMaxLength = 63;

    /// <summary>
    /// The real hazard for partition suffixes is not the leading character but the 63-byte identifier
    /// limit: the full partition table name is "{baseTable}_{suffix}", and Postgres silently truncates
    /// anything longer. Guard the full name against the longest partitioned table. See #4567.
    /// </summary>
    internal static void AssertSuffixesWithinIdentifierLimit(IEnumerable<string> suffixes, int longestTableNameLength)
    {
        if (longestTableNameLength <= 0)
        {
            return;
        }

        // -1 for the '_' separator between the table name and the suffix
        var maxSuffixLength = PostgresqlIdentifierMaxLength - longestTableNameLength - 1;
        var tooLong = suffixes.Where(s => s.Length > maxSuffixLength).Distinct().ToArray();
        if (tooLong.Length > 0)
        {
            throw new ArgumentException(
                $"The following partition suffix values would produce PostgreSQL table identifiers longer than the {PostgresqlIdentifierMaxLength}-byte limit (causing silent truncation and potential partition collisions): {string.Join(", ", tooLong.Select(s => $"'{s}'"))}. The longest partitioned table name is {longestTableNameLength} characters, so suffixes must be at most {maxSuffixLength} characters.");
        }
    }

    private int LongestPartitionedTableNameLength(PostgresqlDatabase database)
    {
        var manager = _store.Options.TenantPartitions?.Partitions;
        if (manager == null)
        {
            return 0;
        }

        return database.AllObjects().OfType<Table>()
            .Where(x => x.Partitioning is ListPartitioning lp && ReferenceEquals(lp.PartitionManager, manager))
            .Select(x => x.Identifier.Name.Length)
            .DefaultIfEmpty(0)
            .Max();
    }

    /// <summary>
    ///     Drop a tenant partition from all tables that use the Marten managed tenant partitioning. NOTE: you have to supply
    ///     the partition suffix for the tenant, not necessarily the tenant id. In most cases we think this will probably
    ///     be the same value, but you may have to "sanitize" the suffix name
    /// </summary>
    /// <param name="suffixes"></param>
    /// <param name="token"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task RemoveMartenManagedTenantsAsync(string[] suffixes, CancellationToken token)
    {
        if (_store.Options.TenantPartitions == null)
        {
            throw new InvalidOperationException(
                $"Marten-managed per-tenant partitioning is not active in this store. Did you miss a call to {nameof(StoreOptions)}.{nameof(StoreOptions.Policies)}.{nameof(StoreOptions.PoliciesExpression.PartitionMultiTenantedDocumentsUsingMartenManagement)}()?");
        }

        if (_store.Tenancy is not DefaultTenancy)
        {
            // #4880: route the sharded case to the full per-tenant removal path — the
            // symmetric inverse of AddMartenManagedTenantsAsync's sharded routing. Under the
            // sharded 1:1 tenant↔suffix shape, ShardedTenancy.RemoveTenantAsync drops the
            // tenant's partitions + per-tenant event sequence + progression rows on its shard
            // AND deletes the master-registry assignment row (see #4868 for the descriptor
            // shrink that lets running hosts retire the tenant's daemon agents).
            if (_store.Tenancy is ShardedTenancy shardedTenancy)
            {
                foreach (var suffix in suffixes)
                {
                    await shardedTenancy.RemoveTenantAsync(suffix, token).ConfigureAwait(false);
                }

                return;
            }

            throw new InvalidOperationException(
                "RemoveMartenManagedTenantsAsync supports DefaultTenancy and ShardedTenancy. " +
                "MasterTableTenancy tenants own their whole database — call " +
                "IServiceProvider.RemoveTenantAsync(tenantId) instead.");
        }

        var database = (PostgresqlDatabase)_store.Tenancy.Default.Database;


        var logger = _store.Options.LogFactory?.CreateLogger<DocumentStore>() ?? NullLogger<DocumentStore>.Instance;

        // #4683: capture sequence-suffix mapping BEFORE the partition drop. Weasel's drop may
        // clear the tenant from the partition registry, and we still need the suffix to drop
        // the freestanding mt_events_sequence_{suffix} afterwards.
        var capturedSuffixes = Marten.Internal.PerTenantPartitionedCleanup.CaptureSequenceSuffixes(
            _store.Options, database, suffixes);

        await _store.Options.TenantPartitions.Partitions.DropPartitionFromAllTables(database, logger, suffixes,
            token).ConfigureAwait(false);

        // #4683: drop the per-tenant sequence and per-tenant mt_event_progression rows for the
        // removed tenants. The partition drop above leaves the freestanding sequence orphaned
        // and the progression rows untouched; both leak across drop-tenant cycles otherwise.
        await Marten.Internal.PerTenantPartitionedCleanup.RunAsync(
            _store.Options, _store.Tenancy.Default.Database, suffixes, capturedSuffixes, token)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Delete all data for a given tenant id and drop any partitions for that tenant id if
    ///     using by tenant partitioning managed by Marten
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task DeleteAllTenantDataAsync(string tenantId, CancellationToken token)
    {
        var cleaner = new TenantDataCleaner(tenantId, _store);
        return cleaner.ExecuteAsync(token);
    }

    /// <summary>
    ///     Auto-assign a tenant to a database using the configured assignment strategy,
    ///     then create list partitions (and, under <c>UseTenantPartitionedEvents</c>, the
    ///     per-tenant event sequence) in the target database. Only available with sharded
    ///     tenancy.
    /// </summary>
    /// <returns>The database_id the tenant was assigned to</returns>
    public Task<string> AddTenantToShardAsync(string tenantId, CancellationToken ct)
    {
        var sharded = _store.Options.Tenancy as ShardedTenancy
            ?? throw new InvalidOperationException(
                "AddTenantToShardAsync is only available when using MultiTenantedWithShardedDatabases()");

        // #4598: delegate to ShardedTenancy.AddTenantAsync — the jasperfx#413
        // IDynamicTenantSource<string> auto-assign override — so the documented Advanced
        // entry point and the store-agnostic JasperFx admin extension drive ONE code path
        // (auto-assign → createPartitionsForTenant → per-tenant event sequence).
        return sharded.AddTenantAsync(tenantId, ct);
    }

    /// <summary>
    ///     Fully remove a tenant from the sharded pool — the symmetric inverse of
    ///     <see cref="AddTenantToShardAsync(string, CancellationToken)"/>. Drops the tenant's list
    ///     partitions (and, under <c>UseTenantPartitionedEvents</c>, its per-tenant event sequence
    ///     and event-progression rows) from its shard database, deletes the master-registry
    ///     assignment, and shrinks the store's usage descriptor so running hosts can retire the
    ///     tenant's daemon agents without a restart (#4868/#4880). DESTRUCTIVE on the shard —
    ///     use the <c>IDynamicTenantSource</c> disable lifecycle for a non-destructive soft-delete.
    ///     Only available with sharded tenancy.
    /// </summary>
    public async Task RemoveTenantFromShardAsync(string tenantId, CancellationToken ct)
    {
        var sharded = _store.Options.Tenancy as ShardedTenancy
            ?? throw new InvalidOperationException(
                "RemoveTenantFromShardAsync is only available when using MultiTenantedWithShardedDatabases()");

        await sharded.RemoveTenantAsync(tenantId, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Explicitly assign a tenant to a specific database in the pool,
    ///     then create list partitions in the target database. Only available with sharded tenancy.
    /// </summary>
    public async Task AddTenantToShardAsync(string tenantId, string databaseId, CancellationToken ct)
    {
        var sharded = _store.Options.Tenancy as ShardedTenancy
            ?? throw new InvalidOperationException(
                "AddTenantToShardAsync is only available when using MultiTenantedWithShardedDatabases()");

        await sharded.AssignTenantAsync(tenantId, databaseId, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Add a new database to the sharded tenancy pool at runtime.
    ///     Only available with sharded tenancy.
    /// </summary>
    public async Task AddDatabaseToPoolAsync(string databaseId, string connectionString, CancellationToken ct)
    {
        var sharded = _store.Options.Tenancy as ShardedTenancy
            ?? throw new InvalidOperationException(
                "AddDatabaseToPoolAsync is only available when using MultiTenantedWithShardedDatabases()");

        await sharded.AddDatabaseAsync(databaseId, connectionString, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Mark a database as full so no new tenants will be assigned to it.
    ///     Only available with sharded tenancy.
    /// </summary>
    public async Task MarkDatabaseFullAsync(string databaseId, CancellationToken ct)
    {
        var sharded = _store.Options.Tenancy as ShardedTenancy
            ?? throw new InvalidOperationException(
                "MarkDatabaseFullAsync is only available when using MultiTenantedWithShardedDatabases()");

        await sharded.MarkDatabaseFullAsync(databaseId, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     Configure and execute a batch masking of protected data for a subset of the events
    ///     in the event store
    /// </summary>
    /// <returns></returns>
    public Task ApplyEventDataMasking(Action<IEventDataMasking> configure, CancellationToken token = default)
    {
        var masking = new EventDataMasking(_store);
        configure(masking);
        return masking.ApplyAsync(token);
    }
}
