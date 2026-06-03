using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Events.Schema;
using Marten.Storage;
using Marten.Storage.Metadata;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Weasel.Postgresql.Tables.Partitioning;
using Xunit;

namespace TenantPartitionedEventsTests.Config;

/// <summary>
/// Migrated from MultiTenancyTests/use_tenant_partitioned_events_schema_groundwork.cs
/// — config-time validation + schema-shape invariants for
/// <c>UseTenantPartitionedEvents</c>. Every test here builds its own
/// <see cref="StoreOptions"/> or <see cref="DocumentStore"/> on a unique schema:
/// these are configuration-level guards (no shared DocumentStore makes sense
/// because the tests' subject IS the configuration / fresh schema shape).
/// </summary>
public class schema_groundwork_for_partitioned_events
{
    private static string UniqueSchema(string discriminator)
    {
        // tp_sg_<disc>_<pid>_<16-hex>  — keeps under PG's 63-byte identifier limit
        // even for the longest discriminator while staying unique per-test.
        var hex = Guid.NewGuid().ToString("N")[..16];
        return $"tp_sg_{discriminator}_{Environment.ProcessId}_{hex}";
    }

    private static async Task ResetSchemaAsync(string schema, string? extra = null)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        if (extra is not null)
        {
            try { await conn.DropSchemaAsync(extra); } catch (Exception) { }
        }
        try { await conn.DropSchemaAsync(schema); } catch (Exception) { }
    }

    private static StoreOptions BaseOptions(string schema)
    {
        var opts = new StoreOptions();
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = schema;
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.UseTenantPartitionedEvents = true;
        return opts;
    }

    [Fact]
    public void validate_throws_when_combined_with_single_tenancy()
    {
        var opts = new StoreOptions();
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = UniqueSchema("single");
        opts.Events.UseTenantPartitionedEvents = true;
        // Events.TenancyStyle defaults to Single

        var ex = Should.Throw<InvalidOperationException>(() => opts.Validate());
        ex.Message.ShouldContain(nameof(IEventStoreOptions.UseTenantPartitionedEvents));
        ex.Message.ShouldContain(nameof(TenancyStyle.Conjoined));
    }

    [Fact]
    public void validate_throws_on_archived_partitioning_combination()
    {
        var opts = BaseOptions(UniqueSchema("arch"));
        opts.Events.UseArchivedStreamPartitioning = true;

        var ex = Should.Throw<InvalidOperationException>(() => opts.Validate());
        ex.Message.ShouldContain(nameof(IEventStoreOptions.UseTenantPartitionedEvents));
        ex.Message.ShouldContain(nameof(IEventStoreOptions.UseArchivedStreamPartitioning));
    }

    [Fact]
    public void validate_auto_creates_tenant_partitions_when_flag_is_on()
    {
        var opts = BaseOptions(UniqueSchema("auto"));
        opts.TenantPartitions.ShouldBeNull("precondition: not yet validated");

        opts.Validate();

        opts.TenantPartitions.ShouldNotBeNull(
            "Validate() must auto-init MartenManagedTenantListPartitions so the event tables can attach their tenant partitioning");
        opts.TenantPartitions.Partitions.ShouldNotBeNull();
    }

    [Fact]
    public void validate_keeps_user_supplied_tenant_partitions_when_already_configured()
    {
        var opts = BaseOptions(UniqueSchema("user"));
        opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants_pt_sg_" + Guid.NewGuid().ToString("N")[..10]);
        var preExisting = opts.TenantPartitions.ShouldNotBeNull();

        opts.Validate();

        ReferenceEquals(opts.TenantPartitions, preExisting).ShouldBeTrue(
            "Validate() must not replace a user-configured MartenManagedTenantListPartitions instance");
    }

    [Fact]
    public void events_table_has_tenant_list_partitioning_attached()
    {
        // Schema-objects inspection — doesn't touch the database, so no
        // ResetSchemaAsync needed. The store gets disposed at the end of the
        // using block before any apply happens.
        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = UniqueSchema("etbl");
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            o.Events.UseTenantPartitionedEvents = true;
            o.Policies.AllDocumentsAreMultiTenanted();
        });

        var events = store.Options.EventGraph;
        events.UseTenantPartitionedEvents.ShouldBeTrue();
        store.Options.TenantPartitions.ShouldNotBeNull();

        // EventsTable and StreamsTable are schema objects on EventGraph (an IFeatureSchema);
        // reach them through the same enumerable createAllSchemaObjects feeds.
        var schemaObjects = ((Weasel.Core.Migrations.IFeatureSchema)events).Objects;

        var eventsTable = schemaObjects.OfType<Table>()
            .Single(t => t.Identifier.Name == "mt_events");
        var partitioning = eventsTable.Partitioning.ShouldBeOfType<ListPartitioning>();
        partitioning.Columns.ShouldContain(TenantIdColumn.Name);

        var streamsTable = schemaObjects.OfType<Table>()
            .Single(t => t.Identifier.Name == "mt_streams");
        var streamsPartitioning = streamsTable.Partitioning.ShouldBeOfType<ListPartitioning>();
        streamsPartitioning.Columns.ShouldContain(TenantIdColumn.Name);
    }

    [Fact]
    public void events_table_has_no_partitioning_when_flag_is_off()
    {
        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = UniqueSchema("off");
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            // UseTenantPartitionedEvents stays false
            o.Policies.AllDocumentsAreMultiTenanted();
        });

        var events = store.Options.EventGraph;
        events.UseTenantPartitionedEvents.ShouldBeFalse();

        var schemaObjects = ((Weasel.Core.Migrations.IFeatureSchema)events).Objects;
        var eventsTable = schemaObjects.OfType<Table>()
            .Single(t => t.Identifier.Name == "mt_events");
        eventsTable.Partitioning.ShouldBeNull();
    }

    [Fact]
    public void event_progression_table_has_no_tenant_id_column_with_flag_on()
    {
        // #4596 Session 3 design pivot: per-tenant rows are distinguished by
        // ShardName.Identity (which embeds the tenant slot in the
        // {Name}:{ShardKey}:{tenantId} grammar) rather than by a separate
        // tenant_id column. The progression table's PK + columns are
        // byte-for-byte identical to the flag-off case — see Session 3 tests.
        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = UniqueSchema("prog");
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            o.Events.UseTenantPartitionedEvents = true;
            o.Policies.AllDocumentsAreMultiTenanted();
        });

        var schemaObjects = ((Weasel.Core.Migrations.IFeatureSchema)store.Options.EventGraph).Objects;
        var progressionTable = schemaObjects.OfType<Table>()
            .Single(t => t.Identifier.Name == EventProgressionTable.Name);

        progressionTable.Columns.Any(c => c.Name == "tenant_id")
            .ShouldBeFalse(
                "Per-tenant separation lives in ShardName.Identity (`{Name}:{ShardKey}:{tenantId}`), not in a tenant_id column.");
        progressionTable.PrimaryKeyColumns.ShouldBe(new[] { "name" },
            "PK shape is unchanged when the flag flips on — the per-tenant suffix is baked into the name value, not a separate PK column.");
    }

    [Fact]
    public async Task schema_creation_succeeds_with_flag_on_in_empty_database()
    {
        // This one DOES touch the database — actually applies schema changes via
        // EnsureStorageExistsAsync and inspects the live tables. Per-test schema
        // suffix keeps it isolated from sibling tests.
        var schema = UniqueSchema("create");
        await ResetSchemaAsync(schema);

        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = schema;
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            o.Events.UseTenantPartitionedEvents = true;
            o.Policies.AllDocumentsAreMultiTenanted();
        });

        // Register tenants BEFORE the events tables are materialized so the
        // partition manager has both entries by the time EnsureStorageExistsAsync
        // emits the partitioned CREATE TABLE. (The additivelyMigrate path also
        // works post-creation; this is the simpler-to-assert ordering.)
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // Live table inspection by fetching directly via the connection — Weasel's
        // FetchExistingAsync returns the materialized table state including its
        // current partition list.
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var eventsTable = new Table(new PostgresqlObjectName(schema, "mt_events"));
        var liveEvents = await eventsTable.FetchExistingAsync(conn);
        liveEvents.ShouldNotBeNull("mt_events should exist after EnsureStorageExistsAsync");

        var liveEventsPartitioning = liveEvents.Partitioning.ShouldBeOfType<ListPartitioning>();
        liveEventsPartitioning.Columns.ShouldContain(TenantIdColumn.Name);
        liveEventsPartitioning.Partitions.Select(p => p.Suffix).OrderBy(s => s)
            .ShouldBe(new[] { "alpha", "beta" });

        var streamsTable = new Table(new PostgresqlObjectName(schema, "mt_streams"));
        var liveStreams = await streamsTable.FetchExistingAsync(conn);
        liveStreams.ShouldNotBeNull("mt_streams should exist after EnsureStorageExistsAsync");

        var liveStreamsPartitioning = liveStreams.Partitioning.ShouldBeOfType<ListPartitioning>();
        liveStreamsPartitioning.Columns.ShouldContain(TenantIdColumn.Name);
        liveStreamsPartitioning.Partitions.Select(p => p.Suffix).OrderBy(s => s)
            .ShouldBe(new[] { "alpha", "beta" });
    }
}
