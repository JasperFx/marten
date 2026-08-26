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
/// #5268, the partitioned half. <c>Events.BuildHStoreTagIndexConcurrently</c> builds the DCB tag index
/// without blocking writes, but not on a partitioned <c>mt_events</c>: PostgreSQL refuses
/// <c>CREATE INDEX CONCURRENTLY</c> on a partitioned parent outright, and the sequence it accepts instead
/// — an <c>ON ONLY</c> parent index, one concurrent index per partition, an
/// <c>ALTER INDEX ... ATTACH PARTITION</c> for each — needs the partition list, which under
/// <c>UseTenantPartitionedEvents</c> lives in <c>mt_tenant_partitions</c> rather than on the table.
/// <para>
/// Left to run, the sequence stops after step one and leaves the index INVALID: present in
/// <c>pg_indexes</c>, unusable by the planner, and reported as drift by every later apply. So the
/// combination is refused at configuration time, and these tests pin that refusal together with the two
/// things that must keep working around it — the ordinary blocking build, and the out-of-band route from
/// <see cref="EventGraph.HStoreTagIndexName" />, which is what the reporter's sharded, tenant-partitioned
/// deployment actually uses.
/// </para>
/// <para>
/// The refusal is temporary: weasel#520 tracks <c>ListPartitioning.PartitionTableNames</c> consulting the
/// partition manager, which is what makes the list empty. When that ships, the guard comes out and
/// <see cref="the_concurrent_build_is_refused_under_tenant_partitioning" /> becomes the positive case.
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
    public void the_concurrent_build_is_refused_under_tenant_partitioning()
    {
        // Refused rather than quietly downgraded to a blocking build: the whole reason to set this flag is
        // to avoid an outage, so silently taking the lock anyway would be the worst of the three outcomes.
        var ex = Should.Throw<InvalidOperationException>(() => StoreFor(hstore: true, concurrentIndex: true));

        ex.Message.ShouldContain("BuildHStoreTagIndexConcurrently");
        ex.Message.ShouldContain("UseTenantPartitionedEvents");
        // The message has to carry the way out, not just the refusal.
        ex.Message.ShouldContain("IgnoreIndex");
    }

    [Fact]
    public void the_flag_is_harmless_without_hstore_mode()
    {
        // Nothing to build, so nothing to refuse. A store that never turns DCB hstore mode on must not be
        // rejected for a flag that has no index to apply to.
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
        // The route the refusal points at, exercised in the shape it is actually for. Nothing here needs to
        // avoid the lock, so the three steps run as three ordinary statements -- what is being pinned is
        // that Marten then leaves the result alone rather than deciding it is drift.
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
