using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Admin;

/// <summary>
/// #4617 section 3e deferred — pin <see cref="AutoCreate"/> × <c>UseTenantPartitionedEvents</c>.
///
/// <para>
/// AutoCreate values that allow implicit schema mutation (All, CreateOrUpdate,
/// CreateOnly) all let <c>AddMartenManagedTenantsAsync</c> succeed: the parent
/// mt_events / mt_streams tables are created on first storage touch, and the
/// admin call creates the per-tenant partition tables.
/// </para>
///
/// <para>
/// AutoCreate.None: <c>AddMartenManagedTenantsAsync</c> is the user's
/// explicit "yes, mutate schema" signal — already exempt from the gate for
/// the partition-table and per-tenant-sequence DDL it owns. #4641 extended
/// that exemption to the surrounding events feature: a per-feature
/// <c>CreateMigrationAsync(typeof(IEvent))</c> + <c>Migrator.ApplyAllAsync</c>
/// with a scoped <c>CreateOrUpdate</c> override runs BEFORE the partition
/// work, so the parent partitioned tables + plpgsql functions exist by the
/// time partitions are attached. End-to-end append + read now works against
/// a virgin schema under <c>AutoCreate.None</c>.
/// </para>
/// <para>
/// Also pinned: once the schema IS pre-created (typical CLI-apply-then-runtime
/// flow), <c>AutoCreate.None</c> permits the full AddMartenManagedTenantsAsync
/// + append + read flow — covered independently so both the cold-start and
/// pre-warmed deployment shapes have explicit coverage.
/// </para>
/// </summary>
public class autocreate_matrix_under_partitioning
{
    private static string MakeSchema(string suffix) =>
        $"tp_ac_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 28) + "_" + suffix;

    private static DocumentStore BuildStore(string schema, AutoCreate autoCreate)
    {
        return DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.AutoCreateSchemaObjects = autoCreate;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.AddEventType<AutoCreateProbeEvent>();
        });
    }

    private static async Task DropSchemaAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(schema); } catch { }
    }

    [Theory]
    [InlineData(AutoCreate.All)]
    [InlineData(AutoCreate.CreateOrUpdate)]
    [InlineData(AutoCreate.CreateOnly)]
    public async Task permissive_autocreate_values_let_AddMartenManagedTenantsAsync_create_partitions(AutoCreate autoCreate)
    {
        // The pin: for All / CreateOrUpdate / CreateOnly the store can stand up
        // from a virgin schema, AddMartenManagedTenantsAsync creates the per-
        // tenant partitions, and append + read flows succeed without the user
        // having to pre-create anything. This is the "just works" contract for
        // the on-boarding flow under partitioning.
        var schema = MakeSchema("perm");
        await DropSchemaAsync(schema);

        using var store = BuildStore(schema, autoCreate);

        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha");

        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession("alpha"))
        {
            session.Events.StartStream(streamId, new AutoCreateProbeEvent("hello"));
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession("alpha");
        var events = await query.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(1, $"AutoCreate.{autoCreate} must support the standard append+read flow under partitioning");
    }

    [Fact]
    public async Task AutoCreate_None_virgin_schema_admin_call_installs_full_partitioned_append_path()
    {
        // #4641 fix verification: AddMartenManagedTenantsAsync against a
        // virgin schema under AutoCreate.None now ALSO installs the events
        // feature (parent partitioned tables + plpgsql functions) via a
        // per-feature CreateMigrationAsync + Migrator.ApplyAllAsync with
        // a scoped CreateOrUpdate override. The admin call already bypassed
        // the AutoCreate gate for partition tables (Weasel) and per-tenant
        // sequences (EnsureSequencesAsync) — the fix extends that bypass
        // to the surrounding events feature so the next append succeeds
        // end-to-end without the user having to pre-create anything.
        //
        // Before #4641 the admin call left a half-installed state — the
        // next append failed with 42P01 (mt_streams missing) or 42883
        // (function missing). Now the parent tables + functions exist by
        // the time partitions are attached, and append + read both work.
        var schema = MakeSchema("none");
        await DropSchemaAsync(schema);

        using var store = BuildStore(schema, AutoCreate.None);

        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha");

        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession("alpha"))
        {
            session.Events.StartStream(streamId, new AutoCreateProbeEvent("none-virgin"));
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession("alpha");
        var events = await query.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(1,
            "AutoCreate.None against a virgin schema must work end-to-end after #4641 — " +
            "the admin call's bypass scope now includes the events feature");
    }

    [Fact]
    public async Task AutoCreate_None_works_after_a_primer_store_creates_the_schema()
    {
        // The pin: AutoCreate.None is usable in production — the user is
        // expected to apply DDL out-of-band (via CLI / migration tooling)
        // before the runtime app boots with None. Simulate that by using a
        // primer store (AutoCreate.All) to lay down the parent tables, then
        // boot a SECOND store with AutoCreate.None against the same schema
        // and confirm AddMartenManagedTenantsAsync + append + read all work.
        var schema = MakeSchema("primed");
        await DropSchemaAsync(schema);

        // Primer: full DDL.
        using (var primer = BuildStore(schema, AutoCreate.All))
        {
            await primer.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
        }

        // Runtime: AutoCreate.None against the already-created schema.
        using var store = BuildStore(schema, AutoCreate.None);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha");

        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession("alpha"))
        {
            session.Events.StartStream(streamId, new AutoCreateProbeEvent("primed"));
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession("alpha");
        var events = await query.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(1,
            "AutoCreate.None against a pre-existing schema must still let admin partition operations work");
    }
}

public record AutoCreateProbeEvent(string Label);
