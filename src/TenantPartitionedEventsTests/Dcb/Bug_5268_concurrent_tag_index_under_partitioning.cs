#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Dcb;

/// <summary>
/// #5268, the partitioned half — the reporter's own shape, a sharded tenant-partitioned event store where
/// turning HStore mode on should not cost a maintenance window per shard.
/// <para>
/// PostgreSQL refuses <c>CREATE INDEX CONCURRENTLY</c> on a partitioned parent outright, so
/// <c>Events.BuildHStoreTagIndexConcurrently</c> here means the sequence it does accept: an
/// <c>ON ONLY</c> parent index, one concurrent index per tenant partition, and an
/// <c>ALTER INDEX ... ATTACH PARTITION</c> for each. The parent index is created INVALID by design and
/// flips to valid only when the last child is attached — which is why every assertion below reads
/// <c>indisvalid</c> rather than mere existence. An index that exists but is invalid is one the planner
/// ignores, and it is exactly what a half-finished run leaves behind.
/// </para>
/// <para>
/// That last point is not hypothetical. On Weasel 9.26.0 this produced precisely that state, because the
/// sequence was built from the table's DECLARED partitions and under <c>UseTenantPartitionedEvents</c>
/// every partition is owned by the manager backing <c>mt_tenant_partitions</c> — the enumeration came
/// back empty and only step one ran. Fixed in Weasel 9.27.0 (weasel#520), which is the floor for this
/// feature.
/// </para>
/// </summary>
public class Bug_5268_concurrent_tag_index_under_partitioning: IAsyncLifetime
{
    private string _schema = null!;

    public async ValueTask InitializeAsync()
    {
        _schema = $"tp_5268_{Environment.ProcessId}_{Guid.NewGuid():N}";
        if (_schema.Length > 32) _schema = _schema.Substring(0, 32);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(_schema); } catch { }
    }

    public ValueTask DisposeAsync() => default;

    private DocumentStore StoreFor(bool hstore, bool concurrentIndex = false, bool ignoreTagIndex = false)
    {
        return DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;

            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Events.AddEventType<TagIndexOrderPlaced>();

            if (hstore)
            {
                opts.Events.DcbStorageMode = DcbStorageMode.HStore;
                opts.Events.RegisterTagType<TagIndexOrderRef>("tag_index_order_ref");
                opts.Events.BuildHStoreTagIndexConcurrently = concurrentIndex;
            }

            if (ignoreTagIndex)
            {
                opts.Events.IgnoreIndex(EventGraph.HStoreTagIndexName);
            }
        });
    }

    /// <summary>
    /// Seed the store the issue is actually about: the partitioned event tables already exist, with live
    /// per-tenant partitions and rows in them, and hstore mode is turned on afterwards.
    /// </summary>
    private async Task SeedPartitionedStoreAsync()
    {
        using var store = StoreFor(hstore: false);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        foreach (var tenant in new[] { "alpha", "beta" })
        {
            await using var session = store.LightweightSession(tenant);
            session.Events.StartStream(Guid.NewGuid(), new TagIndexOrderPlaced("widget"));
            await session.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Null when the index does not exist, false when it exists but is INVALID — the state a half-finished
    /// concurrent build leaves behind, and the reason "the index is there" is not the assertion to make.
    /// </summary>
    private async Task<bool?> TagIndexIsValidAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand(
            """
            select i.indisvalid from pg_index i
            join pg_class c on c.oid = i.indexrelid
            join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = :schema and c.relname = :name
            """);
        cmd.Parameters.AddWithValue("schema", _schema);
        cmd.Parameters.AddWithValue("name", EventGraph.HStoreTagIndexName);

        return (bool?)await cmd.ExecuteScalarAsync();
    }

    private async Task<int> AttachedChildIndexCountAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand(
            """
            select count(*) from pg_inherits h
            join pg_class parent on parent.oid = h.inhparent
            join pg_namespace n on n.oid = parent.relnamespace
            where n.nspname = :schema and parent.relname = :name
            """);
        cmd.Parameters.AddWithValue("schema", _schema);
        cmd.Parameters.AddWithValue("name", EventGraph.HStoreTagIndexName);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task the_tag_index_is_built_concurrently_across_every_tenant_partition()
    {
        // The headline case. On Weasel 9.26.0 this produced the parent index with indisvalid = false and
        // no children at all, so both halves of the assertion matter: the children have to be attached,
        // AND the parent has to have flipped to valid as a result.
        await SeedPartitionedStoreAsync();

        using var store = StoreFor(hstore: true, concurrentIndex: true);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Two tenant partitions; a manager-owned LIST partitioning has no default partition.
        (await AttachedChildIndexCountAsync()).ShouldBe(2, "one attached index per tenant partition");
        (await TagIndexIsValidAsync())
            .ShouldBe(true, "the parent index is still invalid, so PostgreSQL will not use it");
    }

    [Fact]
    public async Task a_concurrently_built_tag_index_is_not_drift()
    {
        // The half that would make the whole thing useless if it were wrong. Weasel reports an INVALID
        // index as drift, and CONCURRENTLY is not part of what pg_get_indexdef echoes back -- so if the
        // canonical form were wrong in either direction, every apply would rebuild a GIN index on a live
        // event table, which is the outage this exists to avoid, now happening on every startup.
        await SeedPartitionedStoreAsync();

        using var store = StoreFor(hstore: true, concurrentIndex: true);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await Should.NotThrowAsync(() => store.Storage.Database.AssertDatabaseMatchesConfigurationAsync());
        await Should.NotThrowAsync(() => store.Storage.ApplyAllConfiguredChangesToDatabaseAsync());

        (await AttachedChildIndexCountAsync()).ShouldBe(2);
        (await TagIndexIsValidAsync()).ShouldBe(true);
    }

    [Fact]
    public async Task a_tenant_added_after_the_index_gets_its_own_partition_index()
    {
        // Tenant partitions arrive over time, long after the index was built, and the enumeration the
        // sequence is built from is only a point-in-time answer. A new partition is empty, so PostgreSQL
        // builds and attaches its index as part of creating it -- but the parent would go invalid again
        // if that did not happen, so it is worth pinning rather than assuming.
        await SeedPartitionedStoreAsync();

        using var store = StoreFor(hstore: true, concurrentIndex: true);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "gamma");

        (await AttachedChildIndexCountAsync()).ShouldBe(3);
        (await TagIndexIsValidAsync()).ShouldBe(true);

        // And the new tenant can actually use it.
        var orderRef = new TagIndexOrderRef("ORD-gamma");

        await using var session = store.LightweightSession("gamma");
        var evt = session.Events.BuildEvent(new TagIndexOrderPlaced("gamma-widget"));
        evt.WithTag(orderRef);
        session.Events.StartStream(Guid.NewGuid(), evt);
        await session.SaveChangesAsync();

        var found = await session.Events.QueryByTagsAsync(
            new JasperFx.Events.Tags.EventTagQuery().Or<TagIndexOrderRef>(orderRef));
        found.Count.ShouldBe(1);
    }

    [Fact]
    public void the_flag_is_harmless_without_hstore_mode()
    {
        // Nothing to build. A store that never turns DCB hstore mode on must not be affected by a flag
        // that has no index to apply to.
        Should.NotThrow(() =>
        {
            using var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = _schema;
                opts.Events.TenancyStyle = TenancyStyle.Conjoined;
                opts.Events.UseTenantPartitionedEvents = true;
                opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
                opts.Policies.AllDocumentsAreMultiTenanted();
                opts.Events.BuildHStoreTagIndexConcurrently = true;
            });
        });
    }

    [Fact]
    public async Task the_blocking_build_is_unchanged()
    {
        // The default path under partitioning, and the baseline the other two routes are measured against:
        // one plain CREATE INDEX on the partitioned parent, which PostgreSQL propagates to every partition.
        await SeedPartitionedStoreAsync();

        using var store = StoreFor(hstore: true);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await TagIndexIsValidAsync()).ShouldBe(true);
        // Two tenant partitions; a manager-owned LIST partitioning has no default partition.
        (await AttachedChildIndexCountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task the_out_of_band_index_survives_under_partitioning()
    {
        // The other route, still supported: IgnoreIndex plus an operator building the index by hand. It
        // stays worth pinning because it is what a shard already running an older Marten has to use, and
        // because it is the escape hatch if the emitted sequence is ever not what a given site wants.
        // Nothing here needs to avoid the lock, so the three steps run as three ordinary statements --
        // what is pinned is that Marten then leaves the result alone rather than deciding it is drift.
        await SeedPartitionedStoreAsync();

        using var store = StoreFor(hstore: true, ignoreTagIndex: true);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // The column arrives regardless -- it is a metadata-only add, and DCB cannot work without it.
        (await TagIndexIsValidAsync()).ShouldBeNull("the ignored index must not have been created");

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();

            await conn.CreateCommand(
                    $"create index {EventGraph.HStoreTagIndexName} on only {_schema}.mt_events using gin (tags)")
                .ExecuteNonQueryAsync();

            foreach (var suffix in new[] { "alpha", "beta" })
            {
                await conn.CreateCommand(
                        $"create index concurrently idx_mt_events_tags_{suffix} on {_schema}.mt_events_{suffix} using gin (tags)")
                    .ExecuteNonQueryAsync();

                await conn.CreateCommand(
                        $"alter index {_schema}.{EventGraph.HStoreTagIndexName} attach partition {_schema}.idx_mt_events_tags_{suffix}")
                    .ExecuteNonQueryAsync();
            }
        }

        // The parent flips to valid by itself once the last child is attached.
        (await TagIndexIsValidAsync()).ShouldBe(true);
        (await AttachedChildIndexCountAsync()).ShouldBe(2);

        await Should.NotThrowAsync(() => store.Storage.ApplyAllConfiguredChangesToDatabaseAsync());
        await Should.NotThrowAsync(() => store.Storage.Database.AssertDatabaseMatchesConfigurationAsync());

        (await TagIndexIsValidAsync()).ShouldBe(true);

        // And the tagging itself still works -- suppressing the index must not suppress the tags.
        var orderRef = new TagIndexOrderRef("ORD-" + Guid.NewGuid().ToString("N")[..8]);

        await using var session = store.LightweightSession("alpha");
        var evt = session.Events.BuildEvent(new TagIndexOrderPlaced("alpha-widget"));
        evt.WithTag(orderRef);
        session.Events.StartStream(Guid.NewGuid(), evt);
        await session.SaveChangesAsync();

        var found = await session.Events.QueryByTagsAsync(
            new JasperFx.Events.Tags.EventTagQuery().Or<TagIndexOrderRef>(orderRef));
        found.Count.ShouldBe(1);
    }
}

public record TagIndexOrderRef(string Value);

public record TagIndexOrderPlaced(string Product);
