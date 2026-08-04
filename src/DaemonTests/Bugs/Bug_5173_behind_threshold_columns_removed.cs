using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Testing;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.Bugs;

/// <summary>
/// #5173 — <c>warning_behind_threshold</c> and <c>critical_behind_threshold</c> were created in DDL,
/// listed in every extended-tracking SELECT, and hydrated onto <c>ShardState</c> — and written by
/// nothing, in any repo. Every read returned NULL, always. They are gone; what is left is a pin that
/// removing two columns from the middle of a positional selector did not shift the ordinals of the
/// <c>failure_*</c> columns that follow them.
/// </summary>
public class Bug_5173_behind_threshold_columns_removed: OneOffConfigurationsContext
{
    [Fact]
    public async Task the_threshold_columns_are_no_longer_created()
    {
        StoreOptions(x => x.Events.EnableExtendedProgressionTracking = true);
        await theStore.EnsureStorageExistsAsync(typeof(IEvent));

        var columns = await progressionColumnsAsync();

        columns.ShouldNotContain("warning_behind_threshold");
        columns.ShouldNotContain("critical_behind_threshold");

        // ...and the eight columns that carry something are untouched.
        foreach (var expected in new[]
                 {
                     "heartbeat", "agent_status", "pause_reason", "running_on_node", "failure_category",
                     "failure_event_sequence", "failure_event_type", "failure_event_tenant_id"
                 })
        {
            columns.ShouldContain(expected);
        }
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
