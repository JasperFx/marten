using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx;
using JasperFx.Core;
using JasperFx.Descriptors;
using JasperFx.MultiTenancy;
using Marten.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Core.MultiTenancy;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Weasel.Postgresql.Tables.Partitioning;
using Table = Weasel.Postgresql.Tables.Table;

namespace Marten.Storage;

/// <summary>
/// Multi-tenancy implementation that distributes tenants across a pool of databases
/// with conjoined tenancy and native PG list partitioning per tenant within each database.
/// </summary>
public class ShardedTenancy : ITenancy, ITenancyWithMasterDatabase, ITenantDatabasePool,
    IDynamicTenantSource<string>, IAsyncDisposable
{
    private readonly StoreOptions _options;
    private readonly ShardedTenancyOptions _configuration;
    private readonly Lazy<NpgsqlDataSource> _dataSource;
    private readonly Lazy<PoolLookupDatabase> _poolDatabase;
    private readonly string _schemaName;

    // tenant_id -> (MartenDatabase, databaseId)
    private ImHashMap<string, MartenDatabase> _tenantToDatabase = ImHashMap<string, MartenDatabase>.Empty;
    // database_id -> MartenDatabase
    private ImHashMap<string, MartenDatabase> _databasesById = ImHashMap<string, MartenDatabase>.Empty;

    private bool _hasAppliedChanges;
    private bool _hasSeeded;

    // Advisory lock key for tenant assignment serialization
    private const int AdvisoryLockKey = 4173_0001; // Unique to sharded tenancy

    public ShardedTenancy(StoreOptions options, ShardedTenancyOptions configuration)
    {
        _options = options;
        _configuration = configuration;
        _schemaName = configuration.SchemaName;

        if (configuration.DataSource != null)
        {
            _dataSource = new Lazy<NpgsqlDataSource>(() => configuration.DataSource);
        }
        else if (configuration.ConnectionString.IsNotEmpty())
        {
            _dataSource = new Lazy<NpgsqlDataSource>(() =>
                _options.NpgsqlDataSourceFactory.Create(configuration.ConnectionString));
        }
        else
        {
            throw new ArgumentException(
                "Either a ConnectionString or DataSource must be provided for ShardedTenancy");
        }

        Cleaner = new CompositeDocumentCleaner(this, _options);

        _poolDatabase = new Lazy<PoolLookupDatabase>(() =>
            new PoolLookupDatabase(_options, _dataSource.Value, _schemaName));
    }

    public void Dispose()
    {
        foreach (var entry in _databasesById.Enumerate()) entry.Value.Dispose();

        if (_dataSource.IsValueCreated)
        {
            _dataSource.Value.Dispose();
        }
    }

    // #4874: async teardown mirror of Dispose() so DocumentStore.DisposeAsync() runs each shard
    // database's OwnsDataSource-aware async disposal rather than the blocking sync chain.
    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _databasesById.Enumerate())
        {
            await entry.Value.DisposeAsync().ConfigureAwait(false);
        }

        if (_dataSource.IsValueCreated)
        {
            await _dataSource.Value.DisposeAsync().ConfigureAwait(false);
        }
    }

    #region ITenancy

    public Tenant Default => throw new NotSupportedException(
        "Default tenant is not supported with sharded multi-tenancy. All operations require a tenant ID.");

    public IDocumentCleaner Cleaner { get; }

    public Tenant GetTenant(string tenantId)
    {
        tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        if (_tenantToDatabase.TryFind(tenantId, out var database))
        {
            return new Tenant(tenantId, database);
        }

        database = findOrAssignTenantDatabaseAsync(tenantId).GetAwaiter().GetResult();
        return new Tenant(tenantId, database);
    }

    public async ValueTask<Tenant> GetTenantAsync(string tenantId)
    {
        tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        if (_tenantToDatabase.TryFind(tenantId, out var database))
        {
            return new Tenant(tenantId, database);
        }

        database = await findOrAssignTenantDatabaseAsync(tenantId).ConfigureAwait(false);
        return new Tenant(tenantId, database);
    }

    public async ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier)
    {
        tenantIdOrDatabaseIdentifier = _options.TenantIdStyle.MaybeCorrectTenantId(tenantIdOrDatabaseIdentifier);

        // Try tenant lookup first
        if (_tenantToDatabase.TryFind(tenantIdOrDatabaseIdentifier, out var database))
        {
            return database;
        }

        // Try database id lookup
        if (_databasesById.TryFind(tenantIdOrDatabaseIdentifier, out database))
        {
            return database;
        }

        return await findOrAssignTenantDatabaseAsync(tenantIdOrDatabaseIdentifier).ConfigureAwait(false);
    }

    public async ValueTask<IMartenDatabase> FindDatabase(DatabaseId id)
    {
        var database = _databasesById.Enumerate().Select(x => x.Value).FirstOrDefault(x => x.Id == id);
        if (database != null) return database;

        await BuildDatabases().ConfigureAwait(false);

        database = _databasesById.Enumerate().Select(x => x.Value).FirstOrDefault(x => x.Id == id);
        if (database == null)
        {
            throw new ArgumentOutOfRangeException(nameof(id), $"Requested database {id.Identity} cannot be found");
        }

        return database;
    }

    public bool IsTenantStoredInCurrentDatabase(IMartenDatabase database, string tenantId)
    {
        tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        if (_tenantToDatabase.TryFind(tenantId, out var assignedDb))
        {
            return assignedDb.Id == database.Id;
        }
        return false;
    }

    public async ValueTask<IReadOnlyList<IDatabase>> BuildDatabases()
    {
        await maybeApplyChanges().ConfigureAwait(false);
        await maybeSeedDatabases().ConfigureAwait(false);

        await using var conn = _dataSource.Value.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        try
        {
            // Load all databases from pool
            await using var poolReader = await ((DbCommand)conn
                    .CreateCommand($"select database_id, connection_string, is_full, tenant_count from {_schemaName}.{DatabasePoolTable.TableName}"))
                .ExecuteReaderAsync().ConfigureAwait(false);

            while (await poolReader.ReadAsync().ConfigureAwait(false))
            {
                var databaseId = await poolReader.GetFieldValueAsync<string>(0).ConfigureAwait(false);

                if (_databasesById.TryFind(databaseId, out _)) continue;

                var connectionString = await poolReader.GetFieldValueAsync<string>(1).ConfigureAwait(false);
                connectionString = _configuration.CorrectedConnectionString(connectionString);

                var database = new MartenDatabase(_options,
                    _options.NpgsqlDataSourceFactory.Create(connectionString), databaseId);

                _databasesById = _databasesById.AddOrUpdate(databaseId, database);
            }

            await poolReader.CloseAsync().ConfigureAwait(false);

            // Load all tenant assignments — only active (non-disabled). Disabled
            // tenants are excluded from the in-memory tenant→database cache so
            // GetTenantAsync surfaces UnknownTenantIdException for them, mirroring
            // MasterTableTenancy's soft-delete semantics (#4607).
            await using var assignReader = await ((DbCommand)conn
                    .CreateCommand($"select tenant_id, database_id from {_schemaName}.{TenantAssignmentTable.TableName} where {MartenTenantAssignmentTable.DisabledColumn} = false"))
                .ExecuteReaderAsync().ConfigureAwait(false);

            // #4868: the fresh assignment read below is the single source of truth for
            // "which active tenants live on which shard" — collect it so the in-memory
            // registry can be RECONCILED against it (shrunk, not just grown) afterwards.
            var activeAssignments = new Dictionary<string, string>(StringComparer.Ordinal);

            while (await assignReader.ReadAsync().ConfigureAwait(false))
            {
                var tenantId = await assignReader.GetFieldValueAsync<string>(0).ConfigureAwait(false);
                tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
                var databaseId = await assignReader.GetFieldValueAsync<string>(1).ConfigureAwait(false);

                activeAssignments[tenantId] = databaseId;

                if (_databasesById.TryFind(databaseId, out var database))
                {
                    database.TenantIds.Fill(tenantId);
                    _tenantToDatabase = _tenantToDatabase.AddOrUpdate(tenantId, database);
                }
            }

            await assignReader.CloseAsync().ConfigureAwait(false);

            // #4868: reconcile the in-memory registry against the fresh read. TenantIds /
            // _tenantToDatabase used to be add-only (`Fill`), so a running store's usage
            // descriptor (DescribeDatabasesAsync → TryCreateUsage) never shrank after
            // RemoveTenantAsync / DisableTenantAsync — locally OR from another node — and
            // hosts that retire per-tenant daemon agents off the descriptor's supported-set
            // diff (Wolverine-managed distribution, per-cycle) could never see a tenant
            // leave until a process restart. Pruning here makes every BuildDatabases /
            // DescribeDatabasesAsync call a fresh-read re-enumeration, mirroring #4864's
            // rationale for the single-database path: one source, invalidation-free.
            foreach (var pair in _databasesById.Enumerate())
            {
                var staleTenantIds = pair.Value.TenantIds
                    .Where(t => !activeAssignments.TryGetValue(t, out var assignedDb) ||
                                !assignedDb.EqualsIgnoreCase(pair.Key))
                    .ToArray();

                foreach (var stale in staleTenantIds)
                {
                    pair.Value.TenantIds.Remove(stale);
                }
            }

            foreach (var pair in _tenantToDatabase.Enumerate().ToArray())
            {
                if (!activeAssignments.ContainsKey(pair.Key))
                {
                    _tenantToDatabase = _tenantToDatabase.Remove(pair.Key);
                }
            }
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }

        var list = _databasesById.Enumerate().Select(x => x.Value).OfType<IDatabase>().ToList();
        list.Insert(0, _poolDatabase.Value);
        return list;
    }

    #endregion

    #region ITenancyWithMasterDatabase

    public PostgresqlDatabase TenantDatabase => _poolDatabase.Value;

    #endregion

    #region ITenantDatabasePool

    public async ValueTask<IReadOnlyList<PooledDatabase>> ListDatabasesAsync(CancellationToken ct)
    {
        await maybeApplyChanges().ConfigureAwait(false);
        await maybeSeedDatabases().ConfigureAwait(false);

        var list = new List<PooledDatabase>();

        await using var conn = _dataSource.Value.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var reader = await ((DbCommand)conn
                .CreateCommand($"select database_id, connection_string, is_full, tenant_count from {_schemaName}.{DatabasePoolTable.TableName}"))
            .ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new PooledDatabase(
                await reader.GetFieldValueAsync<string>(0, ct).ConfigureAwait(false),
                await reader.GetFieldValueAsync<string>(1, ct).ConfigureAwait(false),
                await reader.GetFieldValueAsync<bool>(2, ct).ConfigureAwait(false),
                await reader.GetFieldValueAsync<int>(3, ct).ConfigureAwait(false)
            ));
        }

        await conn.CloseAsync().ConfigureAwait(false);
        return list;
    }

    public async ValueTask AddDatabaseAsync(string databaseId, string connectionString, CancellationToken ct)
    {
        await maybeApplyChanges().ConfigureAwait(false);

        await _dataSource.Value
            .CreateCommand(
                $"insert into {_schemaName}.{DatabasePoolTable.TableName} (database_id, connection_string) values (:id, :conn) on conflict (database_id) do update set connection_string = :conn")
            .With("id", databaseId)
            .With("conn", connectionString)
            .ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        // Create and cache the MartenDatabase
        var corrected = _configuration.CorrectedConnectionString(connectionString);
        var database = new MartenDatabase(_options,
            _options.NpgsqlDataSourceFactory.Create(corrected), databaseId);
        _databasesById = _databasesById.AddOrUpdate(databaseId, database);
    }

    public async ValueTask MarkDatabaseFullAsync(string databaseId, CancellationToken ct)
    {
        await maybeApplyChanges().ConfigureAwait(false);
        await maybeSeedDatabases().ConfigureAwait(false);

        await _dataSource.Value
            .CreateCommand(
                $"update {_schemaName}.{DatabasePoolTable.TableName} set is_full = true where database_id = :id")
            .With("id", databaseId)
            .ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask<string?> FindDatabaseForTenantAsync(string tenantId, CancellationToken ct)
    {
        tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        await maybeApplyChanges().ConfigureAwait(false);

        // #4607: filter out soft-deleted assignments so disabled tenants are not
        // resolvable — mirrors MasterTableTenancy's `disabled = false` gate.
        var result = await _dataSource.Value
            .CreateCommand(
                $"select database_id from {_schemaName}.{TenantAssignmentTable.TableName} where tenant_id = :id and {MartenTenantAssignmentTable.DisabledColumn} = false")
            .With("id", tenantId)
            .ExecuteScalarAsync(ct).ConfigureAwait(false);

        return result as string;
    }

    public async ValueTask AssignTenantAsync(string tenantId, string databaseId, CancellationToken ct)
    {
        tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        await maybeApplyChanges().ConfigureAwait(false);
        await maybeSeedDatabases().ConfigureAwait(false);

        await using var conn = _dataSource.Value.CreateConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // Acquire advisory lock
        await conn.CreateCommand($"select pg_advisory_lock({AdvisoryLockKey})")
            .ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        try
        {
            // #4763: capture the tenant's current shard (if any) BEFORE the upsert so we can recompute the
            // SOURCE shard's tenant_count when this is a re-assignment. Without this only the target shard
            // is recomputed below, leaving the source shard's count permanently inflated and making
            // UseSmallestDatabaseAssignment mis-rank it as fuller than it really is.
            var previousDatabaseId = (await conn.CreateCommand(
                    $"select database_id from {_schemaName}.{TenantAssignmentTable.TableName} where tenant_id = :tid")
                .With("tid", tenantId)
                .ExecuteScalarAsync(ct).ConfigureAwait(false)) as string;

            // #4607: explicit assignment also clears the disabled flag so re-assigning
            // a soft-deleted tenant via this API reactivates it. Pairs with the
            // tolerant DisableTenantAsync / EnableTenantAsync semantics — explicit
            // intent overrides prior soft-delete.
            await conn.CreateCommand(
                    $"insert into {_schemaName}.{TenantAssignmentTable.TableName} (tenant_id, database_id) values (:tid, :did) on conflict (tenant_id) do update set database_id = :did, {MartenTenantAssignmentTable.DisabledColumn} = false")
                .With("tid", tenantId)
                .With("did", databaseId)
                .ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await conn.CreateCommand(
                    $"update {_schemaName}.{DatabasePoolTable.TableName} set tenant_count = (select count(*) from {_schemaName}.{TenantAssignmentTable.TableName} where database_id = :did and {MartenTenantAssignmentTable.DisabledColumn} = false) where database_id = :did")
                .With("did", databaseId)
                .ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            // #4763: re-assignment moved the tenant off its previous shard — recompute that shard's
            // tenant_count too so it reflects the tenant having left.
            if (previousDatabaseId != null && !previousDatabaseId.EqualsIgnoreCase(databaseId))
            {
                await conn.CreateCommand(
                        $"update {_schemaName}.{DatabasePoolTable.TableName} set tenant_count = (select count(*) from {_schemaName}.{TenantAssignmentTable.TableName} where database_id = :prev and {MartenTenantAssignmentTable.DisabledColumn} = false) where database_id = :prev")
                    .With("prev", previousDatabaseId)
                    .ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            await conn.CreateCommand($"select pg_advisory_unlock({AdvisoryLockKey})")
                .ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);
        }

        // Create the tenant's partitions + per-tenant event sequence in the target database — OUTSIDE the
        // global advisory lock. The lock exists to keep the master registry rows (assignment + pool
        // counts) consistent; the partition DDL runs against the tenant's own shard database, is
        // idempotent, and can take tens of seconds on a store with many partitioned document tables.
        // Holding the global lock through it serializes ALL tenant provisioning store-wide (a 500-tenant
        // bulk migration measured ~26s between tenant starts on exactly this, while the median tenant's
        // actual work took 4s) and pushes concurrent waiters past their command timeout. Crash-safety is
        // unchanged: with the assignment row committed but partitions missing, a re-run of
        // AssignTenantAsync (or any idempotent provisioning repair) completes the tenant — and since
        // #4942 the auto-assign path (findOrAssignTenantDatabaseAsync) runs the same repair on first
        // sight of an already-assigned tenant, so both AddTenantToShardAsync overloads honor it.
        if (_databasesById.TryFind(databaseId, out var database))
        {
            database.TenantIds.Fill(tenantId);
            _tenantToDatabase = _tenantToDatabase.AddOrUpdate(tenantId, database);

            await createPartitionsForTenant(database, tenantId, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// #4868 / #4880: full removal of a tenant from the sharded pool — the symmetric inverse of
    /// <see cref="AddTenantAsync(string, CancellationToken)"/> / <c>Advanced.AddTenantToShardAsync</c>.
    /// <list type="bullet">
    ///   <item>Drops the tenant's LIST partitions from all managed tables on its shard database,
    ///     including the shard's own <c>mt_tenant_partitions</c> registry rows — so the native
    ///     coordinator's per-tenant re-expansion (jasperfx#491's
    ///     <c>PerTenantShardExpansion</c> over <c>ICrossTenantRebuildSource</c>) stops seeing the
    ///     tenant and its running daemon agents are reaped on the next leadership polling cycle.</item>
    ///   <item>Under <c>UseTenantPartitionedEvents</c>, also drops the tenant's
    ///     <c>mt_events_sequence_{tenant}</c> and deletes its per-tenant
    ///     <c>mt_event_progression</c> + <c>HighWaterMark:{tenant}</c> rows (the #4683 cleanup) —
    ///     progression rows are CLEANED on removal, not retained; a re-added tenant starts fresh.</item>
    ///   <item>Deletes the master-registry assignment row and recomputes the shard's
    ///     <c>tenant_count</c> (mirrors #4763's recompute-on-reassignment).</item>
    ///   <item>Shrinks the in-memory registry so this store's usage descriptor
    ///     (<see cref="DescribeDatabasesAsync"/> → <c>TryCreateUsage</c>) stops reporting the
    ///     tenant immediately — hosts that retire agents off the descriptor diff converge without
    ///     a restart. Other nodes converge via the fresh-read reconciliation in
    ///     <see cref="BuildDatabases"/>.</item>
    /// </list>
    /// This is DESTRUCTIVE on the shard (partition drop == data drop), matching the single-database
    /// removal contract of <c>AdvancedOperations.RemoveMartenManagedTenantsAsync</c>. Use
    /// <see cref="DisableTenantAsync"/> for the non-destructive soft-delete that retains all data
    /// and progression state. Idempotent — a re-run for an unknown/already-removed tenant no-ops.
    /// </summary>
    public async ValueTask RemoveTenantAsync(string tenantId, CancellationToken ct)
    {
        tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        await maybeApplyChanges().ConfigureAwait(false);

        // Resolve the tenant's shard BEFORE deleting the assignment row — deliberately NOT
        // filtered on the disabled flag, so removing a soft-deleted tenant still cleans its shard.
        var databaseId = (await _dataSource.Value
            .CreateCommand(
                $"select database_id from {_schemaName}.{TenantAssignmentTable.TableName} where tenant_id = :id")
            .With("id", tenantId)
            .ExecuteScalarAsync(ct).ConfigureAwait(false)) as string;

        if (databaseId != null)
        {
            // Ensure the shard database is materialized in the local cache (this store may never
            // have resolved the tenant) — mirrors findOrAssignTenantDatabaseAsync's cache repair.
            if (!_databasesById.TryFind(databaseId, out var database))
            {
                await BuildDatabases().ConfigureAwait(false);
                _databasesById.TryFind(databaseId, out database);
            }

            if (database != null)
            {
                // Shard-side DDL FIRST (outside the master advisory lock — same rationale as
                // AssignTenantAsync's partition DDL: the lock guards registry rows, not the
                // shard-local idempotent DDL). If this throws, the assignment row is still
                // intact and the removal can simply be re-run.
                await dropPartitionsForTenant(database, tenantId, ct).ConfigureAwait(false);
            }
        }

        await _dataSource.Value
            .CreateCommand(
                $"delete from {_schemaName}.{TenantAssignmentTable.TableName} where tenant_id = :id")
            .With("id", tenantId)
            .ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        if (databaseId != null)
        {
            // #4763-style recompute: without this the source shard's tenant_count stays
            // permanently inflated after removals and UseSmallestDatabaseAssignment mis-ranks it.
            await _dataSource.Value.CreateCommand(
                    $"update {_schemaName}.{DatabasePoolTable.TableName} set tenant_count = (select count(*) from {_schemaName}.{TenantAssignmentTable.TableName} where database_id = :did and {MartenTenantAssignmentTable.DisabledColumn} = false) where database_id = :did")
                .With("did", databaseId)
                .ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // #4868: shrink the in-memory registry so DescribeDatabasesAsync stops reporting the
        // tenant on THIS node immediately (other nodes shrink via BuildDatabases' fresh read).
        _tenantToDatabase = _tenantToDatabase.Remove(tenantId);
        if (databaseId != null && _databasesById.TryFind(databaseId, out var cached))
        {
            cached.TenantIds.Remove(tenantId);
        }
    }

    /// <summary>
    /// Drop the tenant's shard-side footprint — the mirror of <see cref="createPartitionsForTenant"/>.
    /// Sharded provisioning has a 1:1 tenant↔partition-suffix shape (enforced in
    /// <c>AdvancedOperations.AddMartenManagedTenantsAsync</c>'s sharded routing), so the suffix is
    /// always the tenant id. Skips the partition drop when the shard's own
    /// <c>mt_tenant_partitions</c> registry no longer knows the tenant (partial prior removal) so
    /// re-runs stay safe, but always runs the #4683 sequence/progression cleanup, which is
    /// idempotent by construction.
    /// </summary>
    /// <remarks>
    /// Delegates to Weasel's database-scoped by-value drop
    /// (<see cref="DatabaseScopedTenantPartitions.DropPartitionFromAllTablesForValue"/>): it resolves
    /// the sanitized partition suffix from THIS shard's own <c>mt_tenant_partitions</c> registry,
    /// deletes the registry row (what the coordinator's per-tenant re-expansion / ICrossTenantRebuildSource
    /// reads, so agents can be reaped), and drops the partition — symmetric with the create path
    /// (<c>AddPartitionToAllTables</c>). Previously a hand-rolled quoted DETACH/DROP loop lived here only
    /// because the pre-9.16 native drop interpolated the partition name unquoted and failed with 42601
    /// for hyphenated/GUID/uppercase tenant ids; weasel#338 (Weasel 9.16) fixed the native path to
    /// sanitize identically to create, so this now folds back to the native drop.
    /// </remarks>
    private async Task dropPartitionsForTenant(MartenDatabase database, string tenantId, CancellationToken ct)
    {
        if (_options.TenantPartitions == null)
        {
            return;
        }

        IReadOnlyList<string> registered;
        try
        {
            registered = await database.FetchManagedTenantIdsAsync(ct).ConfigureAwait(false);
        }
        catch (NpgsqlException)
        {
            // The shard's partition registry table was never provisioned (assignment row
            // committed but the partition DDL never ran) — nothing shard-side to drop.
            return;
        }

        // Guard on the shard's own registry: skip the native by-value drop (which throws
        // ArgumentOutOfRangeException on an unknown value) when a partial prior removal already
        // forgot the tenant, keeping re-runs idempotent.
        if (registered.Contains(tenantId, StringComparer.Ordinal))
        {
            var logger = _options.LogFactory?.CreateLogger<DocumentStore>() ?? NullLogger<DocumentStore>.Instance;

            try
            {
                await _options.TenantPartitions.Partitions.DropPartitionFromAllTablesForValue(
                    database, logger, tenantId, ct).ConfigureAwait(false);
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                // 42P01: a configured partition-parent table isn't physically present on this shard
                // (registered tenant, but its doc/event tables were never migrated here). There is no
                // partition to detach — mirror the resilience the former to_regclass-guarded drop had.
                // The registry row is cleared by the native drop before it reaches the DDL, and the
                // #4683 cleanup below still runs.
            }
        }

        // #4683 cleanup: the freestanding per-tenant event sequence and the tenant's
        // mt_event_progression rows (per-projection + HighWaterMark:{tenant}) leak otherwise.
        // Gated internally on UseTenantPartitionedEvents.
        await Internal.PerTenantPartitionedCleanup.RunAsync(
            _options, database, [tenantId],
            new Dictionary<string, string> { [tenantId] = tenantId },
            ct).ConfigureAwait(false);
    }

    #endregion

    #region IDynamicTenantSource<string>

    // jasperfx#413 / #4598: ShardedTenancy is an auto-assign source — the connection-value
    // surface (AddTenantAsync(tenantId, databaseId)) maps to AssignTenantAsync, and the
    // auto-assign override (AddTenantAsync(tenantId, ct) → string) maps to the same path
    // that Advanced.AddTenantToShardAsync(tenantId, ct) drives. The DisableTenantAsync /
    // EnableTenantAsync / AllDisabledAsync soft-delete surface (#4607) is backed by the
    // Marten-added `disabled` column on the assignment table.

    /// <summary>
    /// jasperfx#413 / #4598: store-agnostic FindAsync. For ShardedTenancy the "value" is
    /// the database id the tenant is assigned to (NOT the connection string — the pool
    /// owns connection strings via the master database).
    /// </summary>
    async ValueTask<string> ITenantedSource<string>.FindAsync(string tenantId)
    {
        var databaseId = await FindDatabaseForTenantAsync(tenantId, CancellationToken.None).ConfigureAwait(false);
        if (databaseId == null)
        {
            throw new UnknownTenantIdException(tenantId);
        }

        return databaseId;
    }

    Task ITenantedSource<string>.RefreshAsync()
    {
        // Reset the cached tenant→database map so next lookup re-reads from the pool tables.
        _tenantToDatabase = ImHashMap<string, MartenDatabase>.Empty;
        return Task.CompletedTask;
    }

    IReadOnlyList<string> ITenantedSource<string>.AllActive()
        => _databasesById.Enumerate().Select(x => x.Key).ToList();

    IReadOnlyList<Assignment<string>> ITenantedSource<string>.AllActiveByTenant()
        => _tenantToDatabase.Enumerate()
            .Select(pair => new Assignment<string>(pair.Key, pair.Value.Identifier))
            .ToList();

    /// <summary>
    /// jasperfx#413 / #4598: caller-supplied connection value. For ShardedTenancy the
    /// "value" is the target database id, so this maps to <see cref="AssignTenantAsync" />.
    /// </summary>
    Task IDynamicTenantSource<string>.AddTenantAsync(string tenantId, string databaseId)
        => AssignTenantAsync(tenantId, databaseId, CancellationToken.None).AsTask();

    /// <summary>
    /// jasperfx#413 / #4598: auto-assign override. Runs the same auto-assign +
    /// partition/sequence provisioning path that
    /// <c>Advanced.AddTenantToShardAsync(tenantId, ct)</c> drives, and returns the
    /// resolved database id. This is the entrypoint CritterWatch (and any other
    /// store-agnostic admin tool) uses to provision a sharded tenant without sniffing
    /// the concrete tenancy type — see jasperfx#413.
    /// </summary>
    public async Task<string> AddTenantAsync(string tenantId, CancellationToken token = default)
    {
        // findOrAssignTenantDatabaseAsync (the existing internal) runs the full
        // auto-assign + createPartitionsForTenant flow (now including the per-tenant
        // event sequence — #4598). It already caches the resolved database in
        // _tenantToDatabase, so FindDatabaseForTenantAsync hits the pool table only
        // when this method races a concurrent provisioning.
        await findOrAssignTenantDatabaseAsync(tenantId).ConfigureAwait(false);
        return await FindDatabaseForTenantAsync(tenantId, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Tenant '{tenantId}' was not assigned to any database after auto-assignment");
    }

    /// <summary>
    /// #4607: soft-delete the tenant — flip <c>disabled = true</c> on its assignment row
    /// and evict it from the in-memory tenant→database cache so subsequent tenant
    /// resolution surfaces <see cref="UnknownTenantIdException"/>. Mirrors
    /// <see cref="MasterTableTenancy"/>'s lifecycle so the two dynamic sources behave
    /// uniformly behind the store-agnostic <see cref="IDynamicTenantSource{T}"/>
    /// admin extensions. Idempotent — a no-op for an already-disabled or unknown tenant
    /// (no exception; matches MasterTableTenancy's tolerance).
    /// </summary>
    public async Task DisableTenantAsync(string tenantId)
    {
        tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        await maybeApplyChanges().ConfigureAwait(false);

        // `returning database_id` so the in-memory registry + pool counts can shrink below.
        var databaseId = (await _dataSource.Value
            .CreateCommand(
                $"update {_schemaName}.{TenantAssignmentTable.TableName} set {MartenTenantAssignmentTable.DisabledColumn} = true where tenant_id = :id returning database_id")
            .With("id", tenantId)
            .ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false)) as string;

        // Evict from cache (and dispose only if no other tenant is using the same
        // shared shard database — sharded tenancy reuses one MartenDatabase per
        // assigned shard across tenants, unlike MasterTableTenancy's per-tenant DBs).
        _tenantToDatabase = _tenantToDatabase.Remove(tenantId);

        if (databaseId != null)
        {
            // #4868: shrink the shard's in-memory TenantIds so this store's usage descriptor
            // (DescribeDatabasesAsync → TryCreateUsage) stops reporting the disabled tenant —
            // hosts that retire per-tenant daemon agents off the descriptor's supported-set
            // diff converge without a restart. All data, partitions, and progression rows are
            // RETAINED (soft-delete) — EnableTenantAsync restores everything in place.
            if (_databasesById.TryFind(databaseId, out var database))
            {
                database.TenantIds.Remove(tenantId);
            }

            // Disabled tenants are excluded from assignment ranking (AssignTenantAsync's
            // recompute filters on disabled = false), so keep tenant_count in step here too.
            await _dataSource.Value.CreateCommand(
                    $"update {_schemaName}.{DatabasePoolTable.TableName} set tenant_count = (select count(*) from {_schemaName}.{TenantAssignmentTable.TableName} where database_id = :did and {MartenTenantAssignmentTable.DisabledColumn} = false) where database_id = :did")
                .With("did", databaseId)
                .ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// #4607: re-enable a soft-deleted tenant — flip <c>disabled = false</c>. The next
    /// tenant resolution rehydrates the cache via the standard
    /// <see cref="findOrAssignTenantDatabaseAsync"/> path. Idempotent for already-enabled
    /// or unknown tenants.
    /// </summary>
    public async Task EnableTenantAsync(string tenantId)
    {
        tenantId = _options.TenantIdStyle.MaybeCorrectTenantId(tenantId);
        await maybeApplyChanges().ConfigureAwait(false);

        var databaseId = (await _dataSource.Value
            .CreateCommand(
                $"update {_schemaName}.{TenantAssignmentTable.TableName} set {MartenTenantAssignmentTable.DisabledColumn} = false where tenant_id = :id returning database_id")
            .With("id", tenantId)
            .ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false)) as string;

        if (databaseId != null)
        {
            // #4868: symmetric with DisableTenantAsync — re-grow the in-memory registry
            // eagerly so the usage descriptor reports the tenant again on this store
            // without waiting for the next fresh-read reconciliation, and put the tenant
            // back into the assignment-ranking count.
            if (_databasesById.TryFind(databaseId, out var database))
            {
                database.TenantIds.Fill(tenantId);
                _tenantToDatabase = _tenantToDatabase.AddOrUpdate(tenantId, database);
            }

            await _dataSource.Value.CreateCommand(
                    $"update {_schemaName}.{DatabasePoolTable.TableName} set tenant_count = (select count(*) from {_schemaName}.{TenantAssignmentTable.TableName} where database_id = :did and {MartenTenantAssignmentTable.DisabledColumn} = false) where database_id = :did")
                .With("did", databaseId)
                .ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    Task IDynamicTenantSource<string>.RemoveTenantAsync(string tenantId)
        => RemoveTenantAsync(tenantId, CancellationToken.None).AsTask();

    /// <summary>
    /// #4607: enumerate currently soft-deleted tenants — the rows with
    /// <c>disabled = true</c>. Used by the store-agnostic admin extension
    /// <see cref="JasperFx.MultiTenancy.DynamicTenancyAdminExtensions.AllDisabledTenantsAsync"/>.
    /// </summary>
    public async Task<IReadOnlyList<string>> AllDisabledAsync()
    {
        await maybeApplyChanges().ConfigureAwait(false);

        var list = new List<string>();
        await using var conn = _dataSource.Value.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        try
        {
            await using var reader = await ((DbCommand)conn
                    .CreateCommand(
                        $"select tenant_id from {_schemaName}.{TenantAssignmentTable.TableName} where {MartenTenantAssignmentTable.DisabledColumn} = true"))
                .ExecuteReaderAsync().ConfigureAwait(false);

            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                list.Add(await reader.GetFieldValueAsync<string>(0).ConfigureAwait(false));
            }

            await reader.CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }

        return list;
    }

    #endregion

#pragma warning disable MA0032
    #region Internals

    private async Task<MartenDatabase> findOrAssignTenantDatabaseAsync(string tenantId)
    {
        await maybeApplyChanges().ConfigureAwait(false);
        await maybeSeedDatabases().ConfigureAwait(false);

        // #4942: remember whether THIS process had already resolved the tenant before this call.
        // The existing-assignment repair below runs only on first sight per process — the callers
        // on the hot path (GetTenant/GetTenantAsync/FindOrCreateDatabase) consult _tenantToDatabase
        // before ever reaching this method, so the repair costs at most one batch of IF NOT EXISTS
        // catalog checks per tenant per process.
        var previouslyKnown = _tenantToDatabase.TryFind(tenantId, out _);

        // Step 1: Check assignment table
        var databaseId = await FindDatabaseForTenantAsync(tenantId, CancellationToken.None)
            .ConfigureAwait(false);

        if (databaseId != null && _databasesById.TryFind(databaseId, out var database))
        {
            database.TenantIds.Fill(tenantId);
            _tenantToDatabase = _tenantToDatabase.AddOrUpdate(tenantId, database);

            // #4942: an existing assignment row is NOT proof the tenant was fully provisioned —
            // AssignTenantAsync/this method commit the registry row BEFORE the (idempotent,
            // potentially slow) partition DDL, so a crash or failure in between leaves the tenant
            // half-provisioned: assignment present, list partitions and/or the per-tenant event
            // sequence missing. This path used to return early and never repair such a tenant,
            // so every write to a missing document partition failed with 23514 forever (the
            // explicit AssignTenantAsync overload always re-ran the repair; the auto-assign path
            // did not). Run the same idempotent repair here, guarded to first sight per process.
            if (!previouslyKnown)
            {
                await createPartitionsForTenant(database, tenantId, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            return database;
        }

        // Step 2: Auto-assign under advisory lock
        await using var conn = _dataSource.Value.CreateConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await conn.CreateCommand($"select pg_advisory_lock({AdvisoryLockKey})")
            .ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

        // #4942: tracks whether the tenant turned out to already have an active assignment under
        // the lock (raced a concurrent assigner, or its shard database wasn't materialized locally
        // at Step 1) — those tenants get the same guarded repair as Step 1 instead of the
        // unconditional fresh-assignment provisioning.
        var foundExistingAssignment = false;

        try
        {
            // #4607: under the lock, distinguish three cases:
            //   (a) tenant has an active assignment   → use it
            //   (b) tenant has a DISABLED assignment  → throw UnknownTenantIdException
            //       (mirrors MasterTableTenancy; auto-assigning here would silently
            //       resurrect the soft-deleted tenant, possibly onto a different shard)
            //   (c) no assignment at all              → fall through to auto-assign
            var existingState = await ((DbCommand)conn
                .CreateCommand(
                    $"select database_id, {MartenTenantAssignmentTable.DisabledColumn} from {_schemaName}.{TenantAssignmentTable.TableName} where tenant_id = :id")
                .With("id", tenantId))
                .ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);

            string? existingDbId = null;
            var existingDisabled = false;
            try
            {
                if (await existingState.ReadAsync().ConfigureAwait(false))
                {
                    existingDbId = await existingState.GetFieldValueAsync<string>(0).ConfigureAwait(false);
                    existingDisabled = await existingState.GetFieldValueAsync<bool>(1).ConfigureAwait(false);
                }
            }
            finally
            {
                await existingState.CloseAsync().ConfigureAwait(false);
            }

            if (existingDisabled)
            {
                throw new UnknownTenantIdException(tenantId);
            }

            if (existingDbId != null && _databasesById.TryFind(existingDbId, out database))
            {
                database.TenantIds.Fill(tenantId);
                _tenantToDatabase = _tenantToDatabase.AddOrUpdate(tenantId, database);
                foundExistingAssignment = true;
            }
            else
            {
                // Get available databases
                var availableDatabases = new List<PooledDatabase>();
                await using var reader = await ((DbCommand)conn
                        .CreateCommand(
                            $"select database_id, connection_string, is_full, tenant_count from {_schemaName}.{DatabasePoolTable.TableName} where is_full = false"))
                    .ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);

                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    availableDatabases.Add(new PooledDatabase(
                        await reader.GetFieldValueAsync<string>(0).ConfigureAwait(false),
                        await reader.GetFieldValueAsync<string>(1).ConfigureAwait(false),
                        false,
                        await reader.GetFieldValueAsync<int>(3).ConfigureAwait(false)
                    ));
                }

                await reader.CloseAsync().ConfigureAwait(false);

                // Run assignment strategy
                var assignedDbId = await _configuration.AssignmentStrategy
                    .AssignTenantToDatabaseAsync(tenantId, availableDatabases).ConfigureAwait(false);

                // Write assignment
                await conn.CreateCommand(
                        $"insert into {_schemaName}.{TenantAssignmentTable.TableName} (tenant_id, database_id) values (:tid, :did) on conflict (tenant_id) do update set database_id = :did")
                    .With("tid", tenantId)
                    .With("did", assignedDbId)
                    .ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

                // Update tenant count
                await conn.CreateCommand(
                        $"update {_schemaName}.{DatabasePoolTable.TableName} set tenant_count = tenant_count + 1 where database_id = :did")
                    .With("did", assignedDbId)
                    .ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

                // Ensure database is in cache
                if (!_databasesById.TryFind(assignedDbId, out database))
                {
                    // Need to build it from the pool
                    await BuildDatabases().ConfigureAwait(false);
                    if (!_databasesById.TryFind(assignedDbId, out database))
                    {
                        throw new InvalidOperationException(
                            $"Database '{assignedDbId}' was assigned but could not be found in the pool");
                    }
                }

                database.TenantIds.Fill(tenantId);
                _tenantToDatabase = _tenantToDatabase.AddOrUpdate(tenantId, database);
            }
        }
        finally
        {
            await conn.CreateCommand($"select pg_advisory_unlock({AdvisoryLockKey})")
                .ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);
        }

        // Partition DDL outside the global registry lock — same rationale as AssignTenantAsync above:
        // the lock guards the registry rows, not the tenant's shard-local (idempotent) DDL, and holding
        // it through the DDL serializes every tenant assignment store-wide. For a fresh assignment this
        // is the initial provisioning; for an existing assignment discovered under the lock it is the
        // #4942 first-sight-per-process repair, same guard as Step 1.
        if (!foundExistingAssignment || !previouslyKnown)
        {
            await createPartitionsForTenant(database, tenantId, CancellationToken.None).ConfigureAwait(false);
        }

        return database;
    }

    private async Task createPartitionsForTenant(MartenDatabase database, string tenantId, CancellationToken ct)
    {
        if (_options.TenantPartitions == null) return;

        var partitions = _options.TenantPartitions.Partitions;
        var dict = new Dictionary<string, string> { { tenantId, tenantId } };

        await partitions.AddPartitionToAllTables(
            NullLogger.Instance, database, dict, ct).ConfigureAwait(false);

        // #4598: when per-tenant event partitioning is on, the new tenant also
        // needs its `mt_events_sequence_<suffix>` sequence in the SAME shard
        // database (the QuickAppendEventFunction calls
        // nextval(mt_events_sequence_<suffix>) when it inserts events for that
        // tenant). Without this, the tenant's first event append fails with
        // 42P01: relation "{schema}.mt_events_sequence_<tenant>" does not exist.
        // Suffix == tenantId per the dict above; idempotent CREATE IF NOT EXISTS.
        if (_options.Events.UseTenantPartitionedEvents)
        {
            await Events.Schema.PerTenantEventSequences.EnsureSequencesAsync(
                database,
                _options.Events.DatabaseSchemaName,
                dict.Values,
                ct).ConfigureAwait(false);
        }
    }

    private async Task maybeApplyChanges()
    {
        if (!_hasAppliedChanges &&
            (_configuration.AutoCreate ?? _options.AutoCreateSchemaObjects) != AutoCreate.None)
        {
            await _poolDatabase.Value
                .ApplyAllConfiguredChangesToDatabaseAsync(_options.AutoCreateSchemaObjects)
                .ConfigureAwait(false);
            _hasAppliedChanges = true;
        }
    }

    private async Task maybeSeedDatabases()
    {
        if (_hasSeeded || _configuration.SeedDatabases.Count == 0) return;

        foreach (var (databaseId, connectionString) in _configuration.SeedDatabases)
        {
            await AddDatabaseAsync(databaseId, connectionString, CancellationToken.None)
                .ConfigureAwait(false);
        }

        _hasSeeded = true;
    }

#pragma warning restore MA0032
    #endregion

    #region Descriptors

    public DatabaseCardinality Cardinality => DatabaseCardinality.DynamicMultiple;

    public async ValueTask<DatabaseUsage> DescribeDatabasesAsync(CancellationToken token)
    {
        await BuildDatabases().ConfigureAwait(false);

        var list = _databasesById.Enumerate().Select(pair =>
        {
            // MartenDatabase.Describe() already copies TenantIds onto the descriptor —
            // Fill (not AddRange) so each tenant id appears exactly once. The BuildDatabases
            // call above reconciled TenantIds against a fresh assignment read (#4868), so
            // the descriptor shrinks after RemoveTenantAsync/DisableTenantAsync — including
            // removals made from other nodes — without a restart.
            var descriptor = pair.Value.Describe();
            foreach (var tenantId in pair.Value.TenantIds)
            {
                descriptor.TenantIds.Fill(tenantId);
            }

            return descriptor;
        }).ToList();

        return new DatabaseUsage
        {
            Cardinality = DatabaseCardinality.DynamicMultiple,
            Databases = list
        };
    }

    #endregion

    #region Inner classes

    internal class PoolLookupDatabase : PostgresqlDatabase
    {
        private readonly PoolFeatureSchema _feature;

        public PoolLookupDatabase(StoreOptions options, NpgsqlDataSource dataSource, string schemaName)
            : base(options, options.AutoCreateSchemaObjects, options.Advanced.Migrator,
                "ShardedTenancyPool", dataSource)
        {
            _feature = new PoolFeatureSchema(schemaName, options);
        }

        public override IFeatureSchema[] BuildFeatureSchemas()
        {
            return [_feature];
        }
    }

    internal class PoolFeatureSchema : FeatureSchemaBase
    {
        private readonly string _schemaName;

        public PoolFeatureSchema(string schemaName, StoreOptions options)
            : base("ShardedTenancyPool", options.Advanced.Migrator)
        {
            _schemaName = schemaName;
        }

        protected override IEnumerable<ISchemaObject> schemaObjects()
        {
            yield return new DatabasePoolTable(_schemaName);
            // #4607: Marten subclass adds the `disabled` column for soft-delete
            // (Disable/Enable lifecycle) without requiring a Weasel release. The
            // additive column-add migration upgrades existing pools in place.
            yield return new MartenTenantAssignmentTable(_schemaName);
        }
    }

    #endregion
}
