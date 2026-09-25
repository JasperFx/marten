using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DaemonTests.Bugs;

/// <summary>
/// A store that registers no event type and no projection has an inactive event store, so Marten never
/// provisions or migrates its event tables. Under <c>AutoCreate.None</c> an <c>mt_event_progression</c> left
/// behind by an older Marten keeps its old shape, and a monitor that asks such a store for its progression
/// (CritterWatch polls every <see cref="IEventStore" /> in the container) used to select the extended columns
/// from it: <c>42703: column "heartbeat" does not exist</c>.
/// </summary>
public class an_inactive_event_store_reports_no_progression: OneOffConfigurationsContext
{
    private async Task leaveABareProgressionTableBehind()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
                           create schema if not exists {SchemaName};
                           drop table if exists {SchemaName}.mt_event_progression;
                           create table {SchemaName}.mt_event_progression
                               (name varchar primary key, last_seq_id bigint, last_updated timestamptz default now());
                           """;
        await cmd.ExecuteNonQueryAsync();
    }

    private IEventDatabase storeWithoutEvents()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>();
            opts.Events.EnableExtendedProgressionTracking = true;
            opts.AutoCreateSchemaObjects = AutoCreate.None;
        });

        return (IEventDatabase)theStore.Tenancy.Default.Database;
    }

    [Fact]
    public async Task it_reads_no_progression_rows_instead_of_querying_a_table_it_does_not_manage()
    {
        var database = storeWithoutEvents();
        await leaveABareProgressionTableBehind();

        var progress = await database.AllProjectionProgress(CancellationToken.None);

        progress.ShouldBeEmpty();
    }

    [Fact]
    public async Task it_reports_no_projection_dead_letters()
    {
        var database = storeWithoutEvents();
        await leaveABareProgressionTableBehind();

        var counts = await database.FetchDeadLetterCountsAsync(CancellationToken.None);

        counts.ShouldBeEmpty();
    }
}
