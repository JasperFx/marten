using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten.Testing;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.Bugs;

/// <summary>
/// #5173 — <c>warning_behind_threshold</c> and <c>critical_behind_threshold</c> are created, and are
/// neither written nor read. #5184 removed them outright on the grounds that nothing in any repo ever
/// owned the value; the read-side half of that stands (they are not selected and not hydrated), but
/// the DDL is restored.
///
/// <para>
/// The cost of removing them was never the two columns, it was the schema CHANGE. Dropping a column
/// from an existing deployment means <c>alter table … drop column</c> on the next apply, and in
/// PostgreSQL that needs ACCESS EXCLUSIVE on a small, hot table every running daemon writes — O(1)
/// metadata, but the lock queues behind in-flight progression writes and blocks everything behind it
/// while it waits. Two always-NULL columns are cheaper than asking every deployment to take that lock.
/// </para>
///
/// <para>
/// So the contract these tests pin is: the columns exist, the eight that carry something are
/// untouched, and a database that already has this table needs no migration.
/// </para>
/// </summary>
public record ThresholdProbe5173(int Number);

public class Bug_5173_behind_threshold_columns_retained: OneOffConfigurationsContext
{
    [Fact]
    public async Task the_threshold_columns_are_still_created()
    {
        StoreOptions(x =>
        {
            x.Events.EnableExtendedProgressionTracking = true;

            // Registering an event type is what puts the event store's tables into the schema
            // migration at all. Without it mt_event_progression is not compared, every delta comes
            // back None, and any assertion about this table is vacuous.
            x.Events.AddEventType(typeof(ThresholdProbe5173));
        });
        await theStore.EnsureStorageExistsAsync(typeof(IEvent));

        var columns = await progressionColumnsAsync();

        // Unwritten and unread, but present — an upgrade must not have to drop them.
        columns.ShouldContain("warning_behind_threshold");
        columns.ShouldContain("critical_behind_threshold");

        // ...and the eight that carry something are untouched.
        foreach (var expected in new[]
                 {
                     "heartbeat", "agent_status", "pause_reason", "running_on_node", "failure_category",
                     "failure_event_sequence", "failure_event_type", "failure_event_tenant_id"
                 })
        {
            columns.ShouldContain(expected);
        }
    }

    /// <summary>
    /// The load-bearing half — the upgrade path. A database provisioned by any earlier Marten HAS
    /// these two columns, so the question is whether upgrading emits DDL against this table. It must
    /// not.
    ///
    /// <para>
    /// The <c>add column if not exists</c> is what keeps this test honest. Against the configuration
    /// as it stands it is a no-op, because the columns are already there — but if they are ever taken
    /// back out of <c>EventProgressionTable</c>, it restores the shape a real deployment is in, Weasel
    /// resolves the two as extras, and the delta becomes the <c>alter table … drop column</c> this
    /// issue exists to avoid. Without it the test is vacuous: the fixture builds a fresh schema, which
    /// trivially matches whatever the configuration happens to say.
    /// </para>
    /// </summary>
    [Fact]
    public async Task an_upgrade_does_not_drop_the_columns_from_an_existing_table()
    {
        StoreOptions(x =>
        {
            x.Events.EnableExtendedProgressionTracking = true;

            // Registering an event type is what puts the event store's tables into the schema
            // migration at all. Without it mt_event_progression is not compared, every delta comes
            // back None, and any assertion about this table is vacuous.
            x.Events.AddEventType(typeof(ThresholdProbe5173));
        });
        await theStore.EnsureStorageExistsAsync(typeof(IEvent));

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync(TestContext.Current.CancellationToken);
            await conn.CreateCommand(
                    $"alter table {theStore.Events.DatabaseSchemaName}.mt_event_progression "
                    + "add column if not exists warning_behind_threshold bigint null, "
                    + "add column if not exists critical_behind_threshold bigint null")
                .ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        // What an upgrading deployment runs.
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var columns = await progressionColumnsAsync();
        columns.ShouldContain("warning_behind_threshold");
        columns.ShouldContain("critical_behind_threshold");

        // ...and a host that asserts its schema at boot rather than applying still starts. This is
        // the sharper edge of dropping them: AutoCreate.None plus a schema check does not migrate,
        // it refuses to run.
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    private async Task<List<string>> progressionColumnsAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var reader = await conn
            .CreateCommand(
                "select column_name from information_schema.columns where table_schema = :schema and table_name = 'mt_event_progression'")
            .With("schema", theStore.Events.DatabaseSchemaName, NpgsqlTypes.NpgsqlDbType.Varchar)
            .ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var names = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
