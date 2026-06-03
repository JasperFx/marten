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
/// AutoCreate.None: the observed contract is incomplete. The admin call
/// <c>AddMartenManagedTenantsAsync</c> is EXEMPT from the gate for the per-
/// tenant sequence DDL it owns, but the partition-table-migrate path
/// (<see cref="Weasel.Postgresql.Tables.Partitioning.ManagedListPartitions"/>'s
/// <c>MigrateAsync</c>) does NOT find the events tables to migrate when there's
/// no other DDL trigger, AND the surrounding event-store plpgsql functions
/// (<c>mt_quick_append_events</c>, <c>mt_archive_stream</c>) are not installed
/// either. The next append fails with 42P01 (mt_streams missing) or 42883
/// (function missing) depending on what's missing first. Pinned as the
/// known-incomplete state — fixing it cleanly requires either applying the
/// events feature's full schema as part of the admin call (which conflicts
/// with <c>CreateOnly</c>'s diff-tightness rules) or scoping a per-feature
/// apply with a temporary <c>CreateOrUpdate</c> override; that's a non-trivial
/// design decision deferred for a follow-up. See #4641.
/// </para>
/// <para>
/// Also pinned: once the schema IS pre-created (typical CLI-apply-then-runtime
/// flow), AutoCreate.None permits the full AddMartenManagedTenantsAsync +
/// append + read flow.
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
    public async Task AutoCreate_None_virgin_schema_admin_call_does_not_install_full_partitioned_append_path()
    {
        // #4641 known-incomplete contract: AddMartenManagedTenantsAsync against
        // a virgin schema under AutoCreate.None creates the per-tenant sequences
        // (via PerTenantEventSequences.EnsureSequencesAsync, which bypasses
        // AutoCreate explicitly) but leaves the events tables AND surrounding
        // plpgsql functions uninstalled. The next append errors — typically
        // with 42P01 (mt_streams does not exist) which is reached before any
        // function-resolution error.
        //
        // Pin captures the half-installed state so a future fix that completes
        // the admin call's bypass scope (install the events feature in full
        // when UseTenantPartitionedEvents is on) flips the assertion
        // intentionally. The fix is deferred because the natural in-band
        // implementation (call EnsureStorageExistsAsync with an override)
        // conflicts with CreateOnly's diff-tightness rules and a per-feature
        // apply API is not yet exposed.
        var schema = MakeSchema("none");
        await DropSchemaAsync(schema);

        using var store = BuildStore(schema, AutoCreate.None);

        // The admin call itself does not throw — it lays down what it can.
        await Should.NotThrowAsync(async () =>
            await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha"));

        // The next append fails because the events-feature schema isn't there.
        await using var session = store.LightweightSession("alpha");
        session.Events.StartStream(Guid.NewGuid(), new AutoCreateProbeEvent("none-virgin"));

        var ex = await Should.ThrowAsync<Marten.Exceptions.MartenCommandException>(async () =>
            await session.SaveChangesAsync());

        // Either 42P01 (table missing) or 42883 (function missing) — both are
        // shapes of the same half-installed state.
        var message = ex.Message;
        var sawExpectedShape = message.Contains("42P01") || message.Contains("42883");
        sawExpectedShape.ShouldBeTrue(
            "expected a half-installed schema error (42P01 missing table / 42883 missing function), got: " + message);
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
