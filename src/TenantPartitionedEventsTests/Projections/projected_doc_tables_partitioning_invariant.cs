using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Projections;

/// <summary>
/// #4617 section 3c (#4652) — pin the two-sided invariant:
///
/// <list type="bullet">
///   <item><c>UseTenantPartitionedEvents</c> alone partitions ONLY the event-
///     store tables (<c>mt_events</c>, <c>mt_streams</c>). Projection
///     document tables (e.g. <c>mt_doc_*</c>) stay plain conjoined tables
///     even when <c>AllDocumentsAreMultiTenanted</c> is on — tenant
///     isolation for projection docs comes from the <c>tenant_id</c> column
///     on the doc row, not from a Postgres partition.</item>
///   <item>The opt-in <c>Policies.PartitionMultiTenantedDocumentsUsingMartenManagement(schema)</c>
///     extends partitioning to projection doc tables — they become LIST-
///     partitioned by <c>tenant_id</c> alongside the event tables. The same
///     <c>mt_tenant_partitions</c> registry drives both event-table and doc-
///     table partition layout.</item>
/// </list>
///
/// <para>
/// Both halves pinned — without (1) a future change that auto-partitions
/// every multi-tenanted doc would silently widen the schema surface; without
/// (2) a future change that breaks the explicit opt-in path wouldn't be
/// caught.
/// </para>
///
/// <para>
/// Two own-stores because the projection registration + partition policy
/// shape the schema; sharing wouldn't be meaningful.
/// </para>
/// </summary>
public class projected_doc_tables_partitioning_invariant
{
    private static string MakeSchema(string suffix) =>
        ($"tp_dpi_{Environment.ProcessId}_{Guid.NewGuid():N}").Substring(0, 28) + "_" + suffix;

    private static async Task DropSchemaAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(schema); } catch { }
    }

    [Fact]
    public async Task default_partitioning_does_NOT_partition_projection_doc_tables()
    {
        // Default shape: UseTenantPartitionedEvents + AllDocumentsAreMultiTenanted.
        // Doc-table partitioning is NOT enabled. Projection docs use the
        // tenant_id column for isolation, not LIST partitioning.
        var schema = MakeSchema("default");
        await DropSchemaAsync(schema);

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Events.AddEventType<DpiIncrementEvent>();
            opts.Projections.Add<DpiCounterProjection>(ProjectionLifecycle.Inline);
        });

        // Apply schema + write a probe event so the doc table is materialized.
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha");
        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession("alpha"))
        {
            session.Events.StartStream(streamId, new DpiIncrementEvent());
            await session.SaveChangesAsync();
        }

        // mt_events parent IS LIST-partitioned by tenant_id.
        (await IsTablePartitionedByListAsync(schema, "mt_events"))
            .ShouldBeTrue("mt_events must be LIST-partitioned by tenant_id under UseTenantPartitionedEvents");

        // The projection doc table is NOT partitioned — it's a plain conjoined
        // table with a tenant_id column.
        (await IsTablePartitionedByListAsync(schema, "mt_doc_dpicounter"))
            .ShouldBeFalse(
                "projection doc tables must NOT be auto-partitioned by UseTenantPartitionedEvents alone — " +
                "isolation comes from the tenant_id column under conjoined tenancy");
    }

    [Fact]
    public async Task explicit_opt_in_DOES_partition_projection_doc_tables()
    {
        // Opt-in shape: UseTenantPartitionedEvents +
        // PartitionMultiTenantedDocumentsUsingMartenManagement(schema). Both
        // the event tables AND projection doc tables get LIST-partitioned by
        // tenant_id, driven by the same mt_tenant_partitions registry.
        var schema = MakeSchema("optin");
        await DropSchemaAsync(schema);

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement(schema);

            opts.Events.AddEventType<DpiIncrementEvent>();
            opts.Projections.Add<DpiCounterProjection>(ProjectionLifecycle.Inline);
        });

        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");
        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession("alpha"))
        {
            session.Events.StartStream(streamId, new DpiIncrementEvent());
            await session.SaveChangesAsync();
        }

        // mt_events parent IS LIST-partitioned (unchanged).
        (await IsTablePartitionedByListAsync(schema, "mt_events"))
            .ShouldBeTrue();

        // NOW mt_doc_dpicounter IS LIST-partitioned too.
        (await IsTablePartitionedByListAsync(schema, "mt_doc_dpicounter"))
            .ShouldBeTrue(
                "PartitionMultiTenantedDocumentsUsingMartenManagement must extend partitioning to " +
                "projection doc tables");

        // Sanity: per-tenant partition children exist for the doc table.
        var docPartitions = await ListPartitionChildrenAsync(schema, "mt_doc_dpicounter");
        docPartitions.ShouldContain("mt_doc_dpicounter_alpha");
        docPartitions.ShouldContain("mt_doc_dpicounter_beta");
    }

    /// <summary>
    /// Probes <c>pg_class</c>: a table's <c>relkind = 'p'</c> means it's a
    /// partitioned table (the partition parent), 'r' means it's a regular
    /// table.
    /// </summary>
    private static async Task<bool> IsTablePartitionedByListAsync(string schema, string tableName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select relkind from pg_class c join pg_namespace n on c.relnamespace = n.oid " +
            "where n.nspname = @s and c.relname = @t";
        cmd.Parameters.AddWithValue("s", schema);
        cmd.Parameters.AddWithValue("t", tableName);
        var raw = await cmd.ExecuteScalarAsync();
        if (raw == null || raw is DBNull) return false;
        var relkind = (char)raw;
        return relkind == 'p';
    }

    /// <summary>
    /// Returns the names of every partition child attached to a partitioned table.
    /// </summary>
    private static async Task<System.Collections.Generic.IReadOnlyList<string>> ListPartitionChildrenAsync(string schema, string parentTableName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            select child.relname
            from pg_inherits i
            join pg_class parent on i.inhparent = parent.oid
            join pg_namespace pn on parent.relnamespace = pn.oid
            join pg_class child on i.inhrelid = child.oid
            where pn.nspname = @s and parent.relname = @t
            order by child.relname";
        cmd.Parameters.AddWithValue("s", schema);
        cmd.Parameters.AddWithValue("t", parentTableName);
        await using var reader = await cmd.ExecuteReaderAsync();
        var names = new System.Collections.Generic.List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }
}

public record DpiIncrementEvent;

public class DpiCounter
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

public partial class DpiCounterProjection : SingleStreamProjection<DpiCounter, Guid>
{
    public DpiCounterProjection() { Name = "DpiCounter"; }
    public void Apply(DpiCounter c, DpiIncrementEvent _) => c.Count++;
}
