using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Testing;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.Bugs;

/// <summary>
/// #5309 — two <see cref="DocumentStore" />s over one <c>DatabaseSchemaName</c> that disagreed about
/// <c>EnableExtendedProgressionTracking</c> used to undo each other's schema. The flag-off store did not
/// merely decline to add the extended columns to <c>mt_event_progression</c>: its expected table
/// genuinely lacked them, the delta resolved them as extras, and Weasel emitted
/// <c>alter table … drop column</c> for each one. Whichever store applied schema last decided the
/// table's shape, and they kept stripping each other for as long as both ran.
///
/// <para>
/// Nothing warned either side. The flag-off store had no reason to log anything, and the flag-on store
/// then failed every progression read with <c>column "heartbeat" does not exist</c>. Found downstream as
/// CritterWatch#1183, where a sample's publisher and its host shared three schemas and the console went
/// silently dark — the poller catches per-database — rather than noisily broken.
/// </para>
///
/// <para>
/// The blast radius was bounded, and measured rather than assumed: the columns dropped IN PLACE, so the
/// progression rows kept their <c>last_seq_id</c>. It cost monitoring data, not projection positions —
/// projections did not rewind. <c>last_seq_id_survives_the_strip</c> below pins that, because if the fix
/// ever regresses it is the difference between an annoyance and a data-loss incident.
/// </para>
///
/// <para>
/// The fix is that the shape of this table no longer depends on configuration at all: every column is
/// created for every store, and the flag gates only whether the daemon reads and writes them. The same
/// applies to <c>UseOptimizedProjectionRebuilds</c> and its three columns, which had the identical defect
/// for the identical reason and is covered here too.
/// </para>
/// </summary>
public record ProgressionProbe5309(int Number);

public class Bug_5309_progression_columns_survive_a_store_with_tracking_off: OneOffConfigurationsContext
{
    private static readonly string[] ExtendedColumns =
    [
        "heartbeat", "agent_status", "pause_reason", "running_on_node", "warning_behind_threshold",
        "critical_behind_threshold", "failure_category", "failure_event_sequence", "failure_event_type",
        "failure_event_tenant_id"
    ];

    private static readonly string[] RebuildColumns = ["mode", "rebuild_threshold", "assigned_node"];

    [Fact]
    public async Task a_store_with_tracking_off_does_not_strip_the_extended_columns()
    {
        var schema = await trackedStoreCreatesTheTableAsync();

        // The precondition. Without it the assertion below passes vacuously if the flag-on store never
        // wrote the columns in the first place -- which is the exact trap that made #4085's shared-suite
        // coverage useless on two stores.
        var before = await progressionColumnsAsync(schema);
        foreach (var column in ExtendedColumns) before.ShouldContain(column);

        await applySchemaFromAStoreWithTrackingOffAsync(schema);

        var after = await progressionColumnsAsync(schema);
        foreach (var column in ExtendedColumns)
        {
            after.ShouldContain(column,
                $"'{column}' was dropped by a store with EnableExtendedProgressionTracking off");
        }
    }

    /// <summary>
    /// The same defect, the same table, a different flag. <c>UseOptimizedProjectionRebuilds</c> gated
    /// three columns the identical way, so two stores disagreeing about IT stripped those three. Fixed by
    /// the same change and pinned here so it is not gated again on the grounds that only the extended
    /// block was reported.
    /// </summary>
    [Fact]
    public async Task a_store_with_optimized_rebuilds_off_does_not_strip_the_rebuild_columns()
    {
        var schema = await trackedStoreCreatesTheTableAsync();

        var before = await progressionColumnsAsync(schema);
        foreach (var column in RebuildColumns) before.ShouldContain(column);

        await applySchemaFromAStoreWithTrackingOffAsync(schema);

        var after = await progressionColumnsAsync(schema);
        foreach (var column in RebuildColumns)
        {
            after.ShouldContain(column,
                $"'{column}' was dropped by a store with UseOptimizedProjectionRebuilds off");
        }
    }

    /// <summary>
    /// The severity claim in the issue, pinned rather than asserted. The columns dropped in place, so the
    /// progression rows kept their positions and projections did not rewind. If a future change ever makes
    /// the strip recreate the table instead, this is the test that turns a monitoring bug into the
    /// data-loss report it would then be.
    /// </summary>
    [Fact]
    public async Task last_seq_id_survives_the_strip()
    {
        var schema = await trackedStoreCreatesTheTableAsync();

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync(TestContext.Current.CancellationToken);
            await conn.CreateCommand(
                    $"insert into {schema}.mt_event_progression (name, last_seq_id, last_updated) "
                    + "values ('Probe5309:All', 4242, now())")
                .ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await applySchemaFromAStoreWithTrackingOffAsync(schema);

        await using var check = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await check.OpenAsync(TestContext.Current.CancellationToken);
        var seq = await check
            .CreateCommand($"select last_seq_id from {schema}.mt_event_progression where name = 'Probe5309:All'")
            .ExecuteScalarAsync(TestContext.Current.CancellationToken);

        seq.ShouldBe(4242L);
    }

    /// <summary>
    /// And the sharper edge: a host that asserts its schema at boot rather than applying it. Under
    /// AutoCreate.None a stripped table does not migrate on the next start, it refuses to run.
    /// </summary>
    [Fact]
    public async Task the_tracked_store_still_matches_its_configuration_afterwards()
    {
        var schema = await trackedStoreCreatesTheTableAsync();
        await applySchemaFromAStoreWithTrackingOffAsync(schema);

        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    private async Task<string> trackedStoreCreatesTheTableAsync()
    {
        StoreOptions(x =>
        {
            x.Events.EnableExtendedProgressionTracking = true;
            x.Events.UseOptimizedProjectionRebuilds = true;

            // Registering an event type is what puts the event store's tables into the schema migration
            // at all. Without it mt_event_progression is never compared, every delta comes back None, and
            // every assertion in this class is vacuous.
            x.Events.AddEventType(typeof(ProgressionProbe5309));
        });

        await theStore.EnsureStorageExistsAsync(typeof(IEvent));
        return theStore.Events.DatabaseSchemaName;
    }

    /// <summary>
    /// The second process: a seeder, a reporting job or a migration tool beside the main service. It
    /// points at the same schema, does not opt into either flag, and applies schema — which is all it
    /// takes.
    /// </summary>
    private static async Task applySchemaFromAStoreWithTrackingOffAsync(string schema)
    {
        await using var bare = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.Events.EnableExtendedProgressionTracking = false;
            opts.Events.UseOptimizedProjectionRebuilds = false;
            opts.Events.AddEventType(typeof(ProgressionProbe5309));
        });

        await bare.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    private static async Task<List<string>> progressionColumnsAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var reader = await conn
            .CreateCommand(
                "select column_name from information_schema.columns where table_schema = :schema and table_name = 'mt_event_progression'")
            .With("schema", schema, NpgsqlTypes.NpgsqlDbType.Varchar)
            .ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var names = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
