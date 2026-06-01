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

namespace MultiTenancyTests;

/// <summary>
/// #4596 Session 1 — schema groundwork for <c>StoreOptions.Events.UseTenantPartitionedEvents</c>.
/// Validates that flipping the flag (a) attaches tenant_id list-partitioning to
/// <c>mt_events</c> and <c>mt_streams</c> via the existing
/// <see cref="MartenManagedTenantListPartitions"/> machinery; (b) adds the
/// nullable <c>tenant_id</c> column to <c>mt_event_progression</c>; (c) auto-creates
/// the tenant-partition manager when the user hasn't opted documents in; and
/// (d) raises a clear config-time error on incompatible combinations
/// (rich append mode, archived-stream partitioning).
/// Per-tenant sequence creation, the QuickAppend function rewrite, and the
/// admin-override impls land in Sessions 2–4 of the same Phase 1 round.
/// </summary>
public class use_tenant_partitioned_events_schema_groundwork
{
    private const string Schema = "tenant_partitioned_events_session1";

    private static async Task ResetSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync("tenants_pt209s1"); } catch (Exception) { }
        try { await conn.DropSchemaAsync(Schema); } catch (Exception) { }
    }

    private static StoreOptions BaseOptions()
    {
        var opts = new StoreOptions();
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = Schema;
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.UseTenantPartitionedEvents = true;
        return opts;
    }

    [Fact]
    public void validate_throws_when_combined_with_single_tenancy()
    {
        var opts = new StoreOptions();
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = Schema;
        opts.Events.UseTenantPartitionedEvents = true;
        // Events.TenancyStyle defaults to Single

        var ex = Should.Throw<InvalidOperationException>(() => opts.Validate());
        ex.Message.ShouldContain(nameof(IEventStoreOptions.UseTenantPartitionedEvents));
        ex.Message.ShouldContain(nameof(TenancyStyle.Conjoined));
    }

    [Fact]
    public void validate_throws_on_archived_partitioning_combination()
    {
        var opts = BaseOptions();
        opts.Events.UseArchivedStreamPartitioning = true;

        var ex = Should.Throw<InvalidOperationException>(() => opts.Validate());
        ex.Message.ShouldContain(nameof(IEventStoreOptions.UseTenantPartitionedEvents));
        ex.Message.ShouldContain(nameof(IEventStoreOptions.UseArchivedStreamPartitioning));
    }

    [Fact]
    public void validate_auto_creates_tenant_partitions_when_flag_is_on()
    {
        var opts = BaseOptions();
        opts.TenantPartitions.ShouldBeNull("precondition: not yet validated");

        opts.Validate();

        opts.TenantPartitions.ShouldNotBeNull(
            "Validate() must auto-init MartenManagedTenantListPartitions so the event tables can attach their tenant partitioning");
        opts.TenantPartitions.Partitions.ShouldNotBeNull();
    }

    [Fact]
    public void validate_keeps_user_supplied_tenant_partitions_when_already_configured()
    {
        var opts = BaseOptions();
        opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants_pt209s1");
        var preExisting = opts.TenantPartitions.ShouldNotBeNull();

        opts.Validate();

        ReferenceEquals(opts.TenantPartitions, preExisting).ShouldBeTrue(
            "Validate() must not replace a user-configured MartenManagedTenantListPartitions instance");
    }

    [Fact]
    public async Task events_table_has_tenant_list_partitioning_attached()
    {
        await ResetSchemaAsync();

        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = Schema;
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
            o.DatabaseSchemaName = Schema + "_off";
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
    public void event_progression_table_adds_nullable_tenant_id_column_when_flag_is_on()
    {
        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = Schema + "_prog";
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            o.Events.UseTenantPartitionedEvents = true;
            o.Policies.AllDocumentsAreMultiTenanted();
        });

        var schemaObjects = ((Weasel.Core.Migrations.IFeatureSchema)store.Options.EventGraph).Objects;
        var progressionTable = schemaObjects.OfType<Table>()
            .Single(t => t.Identifier.Name == EventProgressionTable.Name);

        var tenantIdColumn = progressionTable.Columns
            .SingleOrDefault(c => c.Name == "tenant_id");
        tenantIdColumn.ShouldNotBeNull(
            "Session 1 adds the column (nullable, default null = store-global). Session 3 promotes it into the PK.");
        tenantIdColumn.AllowNulls.ShouldBeTrue(
            "Existing single-row progression continues to read/write the (name) row with tenant_id IS NULL.");
    }

    [Fact]
    public void event_progression_table_omits_tenant_id_column_when_flag_is_off()
    {
        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = Schema + "_prog_off";
            // flag off
            o.Policies.AllDocumentsAreMultiTenanted();
        });

        var schemaObjects = ((Weasel.Core.Migrations.IFeatureSchema)store.Options.EventGraph).Objects;
        var progressionTable = schemaObjects.OfType<Table>()
            .Single(t => t.Identifier.Name == EventProgressionTable.Name);

        progressionTable.Columns.Any(c => c.Name == "tenant_id")
            .ShouldBeFalse("Column is only added when UseTenantPartitionedEvents is on.");
    }

    [Fact]
    public async Task schema_creation_succeeds_with_flag_on_in_empty_database()
    {
        await ResetSchemaAsync();

        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = Schema;
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            o.Events.UseTenantPartitionedEvents = true;
            o.Policies.AllDocumentsAreMultiTenanted();
        });

        // Register tenants BEFORE the events tables are materialized so the
        // partition manager has both entries by the time EnsureStorageExistsAsync
        // emits the partitioned CREATE TABLE. (The additivelyMigrate path also
        // works post-creation; this is the simpler-to-assert ordering.)
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        await store.Storage.Database.EnsureStorageExistsAsync(typeof(JasperFx.Events.IEvent));

        // Live table inspection by fetching directly via the connection — Weasel's
        // FetchExistingAsync returns the materialized table state including its
        // current partition list.
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var eventsTable = new Table(new PostgresqlObjectName(Schema, "mt_events"));
        var liveEvents = await eventsTable.FetchExistingAsync(conn);
        liveEvents.ShouldNotBeNull("mt_events should exist after EnsureStorageExistsAsync");

        var liveEventsPartitioning = liveEvents.Partitioning.ShouldBeOfType<ListPartitioning>();
        liveEventsPartitioning.Columns.ShouldContain(TenantIdColumn.Name);
        liveEventsPartitioning.Partitions.Select(p => p.Suffix).OrderBy(s => s)
            .ShouldBe(new[] { "alpha", "beta" });

        var streamsTable = new Table(new PostgresqlObjectName(Schema, "mt_streams"));
        var liveStreams = await streamsTable.FetchExistingAsync(conn);
        liveStreams.ShouldNotBeNull("mt_streams should exist after EnsureStorageExistsAsync");

        var liveStreamsPartitioning = liveStreams.Partitioning.ShouldBeOfType<ListPartitioning>();
        liveStreamsPartitioning.Columns.ShouldContain(TenantIdColumn.Name);
        liveStreamsPartitioning.Partitions.Select(p => p.Suffix).OrderBy(s => s)
            .ShouldBe(new[] { "alpha", "beta" });
    }
}
