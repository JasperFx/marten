using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.Progress;
using Marten.Events.Schema;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
/// #4596 Phase 1 Session 3 — per-tenant <c>mt_event_progression</c> keying
/// via <see cref="ShardName.Identity"/> rather than a separate tenant_id
/// column. The PK stays just <c>(name)</c>; per-tenant rows are
/// distinguished because jasperfx#407 Phase 0's
/// <see cref="ShardName.Compose"/> folds the optional tenant slot into the
/// identity (<c>{Name}:{ShardKey}:{tenantId}</c>). The high-water-mark
/// shard's identity hardcodes to <see cref="ShardState.HighWaterMark"/>; Marten
/// will compose <c>$"{ShardState.HighWaterMark}:{tenantId}"</c> manually for
/// per-tenant high-water tracking once Phase 2 lands — but that's a Session
/// 4+ concern, not Session 3.
///
/// <para>
/// Net result for Session 3: zero schema-shape change to <c>mt_event_progression</c>
/// (no tenant_id column, no PK change). The existing
/// <see cref="InsertProjectionProgress"/> /
/// <see cref="UpdateProjectionProgress"/> /
/// <see cref="DeleteProjectionProgress"/> operations are correct as-is for
/// per-tenant — they consume <see cref="ShardName.Identity"/> directly.
/// </para>
/// </summary>
public class use_tenant_partitioned_events_progression_keying
{
    private const string Schema = "tenant_partitioned_events_session3";

    private static async Task ResetSchemaAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(schema); } catch (Exception) { }
    }

    private static DocumentStore BuildStore(string schema)
    {
        return DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = schema;
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            o.Events.UseTenantPartitionedEvents = true;
            o.Policies.AllDocumentsAreMultiTenanted();
        });
    }

    [Fact]
    public void event_progression_table_pk_stays_name_only_with_per_tenant_flag_on()
    {
        var opts = new StoreOptions();
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = Schema;
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.UseTenantPartitionedEvents = true;

        opts.Validate();

        var schemaObjects = ((Weasel.Core.Migrations.IFeatureSchema)opts.EventGraph).Objects;
        var progression = schemaObjects.OfType<Table>()
            .Single(t => t.Identifier.Name == EventProgressionTable.Name);

        progression.PrimaryKeyColumns.ShouldBe(new[] { "name" },
            "Session 3 keeps the existing single-column PK — per-tenant separation lives inside ShardName.Identity.");
        progression.Columns.Any(c => c.Name == "tenant_id")
            .ShouldBeFalse("No tenant_id column on mt_event_progression — per-tenant rows share the table via distinct name values.");
    }

    [Fact]
    public async Task tenant_bearing_shard_names_produce_independent_progression_rows()
    {
        var schema = Schema + "_indep";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // ShardName.Compose with explicit tenant slot — jasperfx#407 Phase 0
        // surfaces tenant as a distinct part of the shard grammar that
        // serializes into Identity as `{Name}:{ShardKey}:{tenantId}`.
        var alphaName = ShardName.Compose("orders_projection", tenantId: "alpha");
        var betaName = ShardName.Compose("orders_projection", tenantId: "beta");

        alphaName.Identity.ShouldBe("orders_projection:All:alpha",
            "ShardName.Compose with a tenant id produces a 3-segment identity.");
        betaName.Identity.ShouldBe("orders_projection:All:beta");

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)store.LightweightSession("alpha"))
        {
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(alphaName, floor: 0, ceiling: 17, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(betaName, floor: 0, ceiling: 42, agent: null!)));
            await session.SaveChangesAsync();
        }

        // The PK is single-column (name); the two ShardName identities are
        // distinct strings; therefore two independent rows.
        var rows = await ReadProgressionRowsAsync(schema);
        rows.Count.ShouldBe(2);
        rows.Single(r => r.name == "orders_projection:All:alpha").last_seq_id.ShouldBe(17L);
        rows.Single(r => r.name == "orders_projection:All:beta").last_seq_id.ShouldBe(42L);
    }

    [Fact]
    public async Task tenantless_shard_name_writes_a_single_row_with_the_legacy_identity()
    {
        var schema = Schema + "_global";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // No tenant slot → Identity is `{Name}:{ShardKey}` exactly like every
        // pre-#4596 store. Single store-global row.
        var globalName = ShardName.Compose("orders_projection");
        globalName.Identity.ShouldBe("orders_projection:All");

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)store.LightweightSession("alpha"))
        {
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(globalName, floor: 0, ceiling: 99, agent: null!)));
            await session.SaveChangesAsync();
        }

        var rows = await ReadProgressionRowsAsync(schema);
        rows.Count.ShouldBe(1);
        rows[0].name.ShouldBe("orders_projection:All");
        rows[0].last_seq_id.ShouldBe(99L);
    }

    [Fact]
    public async Task per_tenant_update_only_touches_that_tenants_row()
    {
        var schema = Schema + "_update";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        var alphaName = ShardName.Compose("orders_projection", tenantId: "alpha");
        var betaName = ShardName.Compose("orders_projection", tenantId: "beta");

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)store.LightweightSession("alpha"))
        {
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(alphaName, floor: 0, ceiling: 10, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(betaName, floor: 0, ceiling: 20, agent: null!)));
            await session.SaveChangesAsync();
        }

        // Bump only alpha's row from 10 → 25. Because the `name` column carries
        // the tenant suffix, WHERE name = 'orders_projection:All:alpha'
        // naturally scopes to one tenant.
        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)store.LightweightSession("alpha"))
        {
            session.QueueOperation(new UpdateProjectionProgress(store.Options.EventGraph,
                new EventRange(alphaName, floor: 10, ceiling: 25, agent: null!)));
            await session.SaveChangesAsync();
        }

        var rows = await ReadProgressionRowsAsync(schema);
        rows.Single(r => r.name == "orders_projection:All:alpha").last_seq_id.ShouldBe(25L);
        rows.Single(r => r.name == "orders_projection:All:beta").last_seq_id.ShouldBe(20L,
            "beta's row must be unaffected by alpha's update — the per-tenant suffix on the name column does the scoping.");
    }

    [Fact]
    public async Task per_tenant_delete_only_drops_that_tenants_row()
    {
        var schema = Schema + "_delete";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        var alphaName = ShardName.Compose("orders_projection", tenantId: "alpha");
        var betaName = ShardName.Compose("orders_projection", tenantId: "beta");

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)store.LightweightSession("alpha"))
        {
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(alphaName, floor: 0, ceiling: 10, agent: null!)));
            session.QueueOperation(new InsertProjectionProgress(store.Options.EventGraph,
                new EventRange(betaName, floor: 0, ceiling: 20, agent: null!)));
            await session.SaveChangesAsync();
        }

        await using (var session = (Marten.Internal.Sessions.DocumentSessionBase)store.LightweightSession("alpha"))
        {
            // Pass the tenant-bearing ShardName.Identity — the existing
            // single-arg DeleteProjectionProgress already scopes correctly
            // because the name column carries the tenant suffix.
            session.QueueOperation(new DeleteProjectionProgress(store.Options.EventGraph, alphaName.Identity));
            await session.SaveChangesAsync();
        }

        var rows = await ReadProgressionRowsAsync(schema);
        rows.Count.ShouldBe(1);
        rows[0].name.ShouldBe("orders_projection:All:beta");
        rows[0].last_seq_id.ShouldBe(20L);
    }

    private static async Task<System.Collections.Generic.IReadOnlyList<(string name, long last_seq_id)>>
        ReadProgressionRowsAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"select name, last_seq_id from {schema}.mt_event_progression where name like 'orders_projection%' order by name";
        await using var reader = await cmd.ExecuteReaderAsync();

        var rows = new System.Collections.Generic.List<(string, long)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1)));
        }
        return rows;
    }
}
