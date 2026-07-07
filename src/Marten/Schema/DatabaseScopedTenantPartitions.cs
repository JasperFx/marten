#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using Microsoft.Extensions.Logging;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables.Partitioning;

namespace Marten.Schema;

/// <summary>
/// Ambient "which database is currently being migrated" marker for
/// <see cref="DatabaseScopedTenantPartitions"/> (#4863/#4855).
///
/// <para>
/// Weasel's <c>IListPartitionManager.Partitions()</c> is synchronous and parameterless, and the
/// per-mapping <c>Table</c> singletons that carry the partitioning are shared across every shard
/// database — so the only way to give the expected-partition set a per-database view without a
/// Weasel API change is an ambient scope. <see cref="Marten.Storage.MartenDatabase"/> stamps the
/// scope from its synchronous <c>BuildFeatureSchemas()</c> / <c>FindFeature()</c> overrides, which
/// Weasel's <c>DatabaseBase</c> calls at the head of every migration operation
/// (<c>ApplyAllConfiguredChangesToDatabaseAsync</c>, <c>ensureStorageExistsAsync</c>,
/// <c>CreateMigrationAsync</c>, script generation). AsyncLocal writes in a synchronous callee flow
/// onward through the caller's awaits, so the whole migration operation — including
/// <c>WriteCreateStatement</c> / <c>CreateDelta</c> partition consultation deep inside Weasel —
/// sees the right database. Concurrent applies to different databases are isolated per async flow.
/// </para>
/// </summary>
internal static class TenantPartitionDatabaseScope
{
    private static readonly AsyncLocal<PostgresqlDatabase?> _current = new();

    public static PostgresqlDatabase? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}

/// <summary>
/// Per-database-aware specialization of Weasel's <see cref="ManagedListPartitions"/> for
/// Marten-managed tenant partitioning (#4863/#4855).
///
/// <para>
/// The base class keeps ONE store-wide partition dictionary and hydrates it at most once, from
/// whichever database happens to be touched first. Under multi-database tenancy (sharded pools,
/// master-table tenancy) that produced two families of bugs:
/// #4863 — a table created lazily on a shard by a fresh store instance saw an EMPTY snapshot and
/// was created partitioned with zero partitions (every write → 23514), because nothing ever read
/// that shard's own <c>mt_tenant_partitions</c>; and
/// #4855 — the store-shared snapshot fed every database's delta, so each shard materialized
/// partitions (and per-tenant event sequences) for every tenant of the whole store — quadratic
/// at scale.
/// </para>
///
/// <para>
/// This subclass keys partition state by database identifier, hydrated from each database's own
/// <c>mt_tenant_partitions</c> table (via the per-database initializer that
/// <c>MartenDatabase</c> registers, and eagerly on the add/drop entry points below). The
/// re-implemented <see cref="IListPartitionManager"/> surface serves the ambient database's view
/// when one is in scope and hydrated, falling back to the legacy store-wide snapshot otherwise —
/// behavior only ever gets more precise, never less.
/// </para>
/// </summary>
public class DatabaseScopedTenantPartitions: ManagedListPartitions, IListPartitionManager
{
    private sealed class DatabaseSet
    {
        public readonly SemaphoreSlim Lock = new(1);
        public volatile bool Hydrated;

        // Swapped copy-on-write under Lock; readers see a consistent snapshot.
        public Dictionary<string, string> Values = new();
    }

    private readonly DbObjectName _tableName;
    private ImHashMap<string, DatabaseSet> _databases = ImHashMap<string, DatabaseSet>.Empty;
    private readonly object _databasesLock = new();

    public DatabaseScopedTenantPartitions(string identifier, DbObjectName tableName): base(identifier, tableName)
    {
        _tableName = tableName;
    }

    private DatabaseSet setFor(string databaseIdentifier)
    {
        if (_databases.TryFind(databaseIdentifier, out var set))
        {
            return set;
        }

        lock (_databasesLock)
        {
            if (_databases.TryFind(databaseIdentifier, out set))
            {
                return set;
            }

            set = new DatabaseSet();
            _databases = _databases.AddOrUpdate(databaseIdentifier, set);
            return set;
        }
    }

    /// <summary>
    /// Read the authoritative tenant → partition-suffix registry of the given database from its
    /// own <c>mt_tenant_partitions</c> table. No-op when this database was already hydrated,
    /// unless <paramref name="force"/> refreshes the snapshot from the table.
    /// </summary>
    public async Task HydrateAsync(IDatabase database, NpgsqlConnection conn, CancellationToken token,
        bool force = false)
    {
        var set = setFor(database.Identifier);
        if (set.Hydrated && !force)
        {
            return;
        }

        await set.Lock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (set.Hydrated && !force)
            {
                return;
            }

            var values = new Dictionary<string, string>();
            try
            {
                await using var reader = await conn
                    .CreateCommand($"select partition_value, partition_suffix from {_tableName.QualifiedName}")
                    .ExecuteReaderAsync(token).ConfigureAwait(false);

                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    var value = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
                    var suffix = value.ToLowerInvariant();
                    if (!await reader.IsDBNullAsync(1, token).ConfigureAwait(false))
                    {
                        suffix = (await reader.GetFieldValueAsync<string>(1, token).ConfigureAwait(false))
                            .ToLowerInvariant();
                    }

                    values[value] = suffix;
                }
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                // Fresh database — the registry table doesn't exist yet, so this database
                // genuinely has no tenants. An empty, hydrated set is the correct answer.
            }

            set.Values = values;
            set.Hydrated = true;
        }
        finally
        {
            set.Lock.Release();
        }
    }

    private async Task hydrateWithFreshConnectionAsync(PostgresqlDatabase database, CancellationToken token,
        bool force = false)
    {
        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        await HydrateAsync(database, conn, token, force).ConfigureAwait(false);
        await conn.CloseAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// The tenant → partition-suffix view for a specific database: its own hydrated registry
    /// snapshot when available, the legacy store-wide snapshot otherwise.
    /// </summary>
    public IReadOnlyDictionary<string, string> PartitionsFor(IDatabase database)
    {
        if (_databases.TryFind(database.Identifier, out var set) && set.Hydrated)
        {
            return set.Values;
        }

        return base.Partitions;
    }

    private IReadOnlyDictionary<string, string>? tryScopedView()
    {
        var database = TenantPartitionDatabaseScope.Current;
        if (database != null && _databases.TryFind(database.Identifier, out var set) && set.Hydrated)
        {
            return set.Values;
        }

        return null;
    }

    /// <summary>
    /// The expected partition set. When a database migration is in flight (ambient database scope
    /// set and that database's registry hydrated), this is that database's OWN tenant set;
    /// otherwise the legacy store-wide snapshot.
    /// </summary>
    public new ReadOnlyDictionary<string, string> Partitions
    {
        get
        {
            var scoped = tryScopedView();
            return scoped != null
                ? new ReadOnlyDictionary<string, string>((IDictionary<string, string>)scoped)
                : base.Partitions;
        }
    }

    IEnumerable<ListPartition> IListPartitionManager.Partitions()
    {
        var source = tryScopedView() ?? (IReadOnlyDictionary<string, string>)base.Partitions;

        // Same shape as the base implementation: group values by suffix.
        foreach (var group in source.GroupBy(x => x.Value))
        {
            yield return new ListPartition(group.Key, group.Select(x => $"'{x.Key}'").ToArray());
        }
    }

    /// <summary>
    /// Database-scoped counterpart of the base upsert: refreshes this database's view from its own
    /// registry, merges the new values, and runs the base registry-write + additive table
    /// reconciliation under the ambient database scope so the delta only ever expects THIS
    /// database's tenants (#4855).
    /// </summary>
    public new async Task<TablePartitionStatus[]> AddPartitionToAllTables(ILogger logger,
        PostgresqlDatabase database, Dictionary<string, string> values, CancellationToken token)
    {
        // Force-refresh so the expected set converges on the authoritative registry even when
        // another node registered tenants on this database since we last looked.
        await hydrateWithFreshConnectionAsync(database, token, force: true).ConfigureAwait(false);
        mergeInto(database, values);

        var prior = TenantPartitionDatabaseScope.Current;
        TenantPartitionDatabaseScope.Current = database;
        try
        {
            return await base.AddPartitionToAllTables(logger, database, values, token).ConfigureAwait(false);
        }
        finally
        {
            TenantPartitionDatabaseScope.Current = prior;
        }
    }

    /// <summary>
    /// Database-scoped counterpart of the base single-value upsert + reconcile.
    /// </summary>
    public new async Task AddPartitionToAllTables(PostgresqlDatabase database, string value, string? suffix,
        CancellationToken token)
    {
        await hydrateWithFreshConnectionAsync(database, token, force: true).ConfigureAwait(false);
        mergeInto(database, new Dictionary<string, string>
        {
            [value] = string.IsNullOrEmpty(suffix) ? value.ToLowerInvariant() : suffix
        });

        var prior = TenantPartitionDatabaseScope.Current;
        TenantPartitionDatabaseScope.Current = database;
        try
        {
            await base.AddPartitionToAllTables(database, value, suffix, token).ConfigureAwait(false);
        }
        finally
        {
            TenantPartitionDatabaseScope.Current = prior;
        }
    }

    /// <summary>
    /// Database-scoped counterpart of the base partition drop — removes the suffixes from this
    /// database's view after the base drop deletes the registry rows and detaches the partitions.
    /// </summary>
    public new async Task DropPartitionFromAllTables(PostgresqlDatabase database, ILogger logger,
        string[] suffixNames, CancellationToken token)
    {
        await hydrateWithFreshConnectionAsync(database, token, force: true).ConfigureAwait(false);

        var prior = TenantPartitionDatabaseScope.Current;
        TenantPartitionDatabaseScope.Current = database;
        try
        {
            await base.DropPartitionFromAllTables(database, logger, suffixNames, token).ConfigureAwait(false);
        }
        finally
        {
            TenantPartitionDatabaseScope.Current = prior;
        }

        removeSuffixes(database, suffixNames);
    }

    /// <summary>
    /// Database-scoped counterpart of the base by-value drop. Resolves the partition suffix from
    /// THIS database's registry — the base implementation resolves from the store-wide snapshot,
    /// which under sharded tenancy may have been hydrated from a different database entirely.
    /// </summary>
    public new async Task DropPartitionFromAllTablesForValue(PostgresqlDatabase database, ILogger logger,
        string value, CancellationToken token)
    {
        await hydrateWithFreshConnectionAsync(database, token, force: true).ConfigureAwait(false);

        if (_databases.TryFind(database.Identifier, out var set) && set.Hydrated
            && set.Values.TryGetValue(value, out var suffix))
        {
            await DropPartitionFromAllTables(database, logger, [suffix], token).ConfigureAwait(false);
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(value),
            $"Could not find a partition with the value '{value}' in database '{database.Identifier}'");
    }

    /// <summary>
    /// Clears the hydration state of every per-database view (as well as the base store-wide
    /// snapshot) so the next touch re-reads each database's own registry.
    /// </summary>
    public new void ForceReload()
    {
        base.ForceReload();
        foreach (var entry in _databases.Enumerate())
        {
            entry.Value.Hydrated = false;
        }
    }

    private void mergeInto(IDatabase database, Dictionary<string, string> values)
    {
        var set = setFor(database.Identifier);
        lock (_databasesLock)
        {
            var updated = new Dictionary<string, string>(set.Values);
            foreach (var pair in values)
            {
                updated[pair.Key] = pair.Value;
            }

            set.Values = updated;
        }
    }

    private void removeSuffixes(IDatabase database, string[] suffixNames)
    {
        var set = setFor(database.Identifier);
        lock (_databasesLock)
        {
            var updated = set.Values
                .Where(pair => !suffixNames.Contains(pair.Value, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            set.Values = updated;
        }
    }
}

/// <summary>
/// Per-database initializer registered by <c>MartenDatabase</c> when Marten-managed tenant
/// partitioning is active. Weasel's <c>DatabaseBase</c> runs registered initializers with a
/// connection to THE database being migrated at the head of every migration operation — this is
/// what guarantees a lazily-created table on a shard hydrates its partitions from that shard's own
/// <c>mt_tenant_partitions</c> registry (#4863). Also runs the legacy base hydration so the
/// store-wide fallback snapshot keeps its historical single-database behavior.
/// </summary>
internal class TenantPartitionsDatabaseInitializer: IDatabaseInitializer<NpgsqlConnection>
{
    private readonly IDatabase _database;
    private readonly DatabaseScopedTenantPartitions _partitions;

    public TenantPartitionsDatabaseInitializer(IDatabase database, DatabaseScopedTenantPartitions partitions)
    {
        _database = database;
        _partitions = partitions;
    }

    public async Task InitializeAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await _partitions.InitializeAsync(connection, token).ConfigureAwait(false);
        await _partitions.HydrateAsync(_database, connection, token).ConfigureAwait(false);
    }
}
