using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Daemon.Progress;
using Marten.Storage;
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
/// from it: <c>42703: column "heartbeat" does not exist</c>. Dead-letter reads hit <c>42P01</c> on a table that
/// was never created (#5509).
/// <para>
/// #5511 widened this to the whole diagnostic surface and moved the decision off configuration and onto the
/// exception, so the two properties below hold together: a read whose storage is missing answers nothing, and a
/// read whose storage is present answers with the real rows even on a store that declares no events.
/// </para>
/// </summary>
public class an_inactive_event_store_reports_no_progression: OneOffConfigurationsContext
{
    private static readonly ShardName theShard = new("Fake", "All", 1);

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

    private MartenDatabase storeWithoutEvents()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>();
            opts.Events.EnableExtendedProgressionTracking = true;
            opts.AutoCreateSchemaObjects = AutoCreate.None;
        });

        return (MartenDatabase)theStore.Tenancy.Default.Database;
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

    /// <summary>
    /// The whole surface, not just the two reads #5509 happened to report. Each of these selects from a table
    /// that an inactive event store under <c>AutoCreate.None</c> never provisions -- <c>mt_events</c>,
    /// <c>mt_streams</c>, <c>mt_events_sequence</c>, <c>mt_doc_deadletterevent</c> -- or from the leftover
    /// three-column <c>mt_event_progression</c>. All of them must answer "nothing" rather than throw.
    /// </summary>
    [Fact]
    public async Task every_diagnostic_read_answers_nothing_rather_than_throwing()
    {
        var database = storeWithoutEvents();
        await leaveABareProgressionTableBehind();
        var token = CancellationToken.None;

        (await database.AllProjectionProgress(token)).ShouldBeEmpty();
        (await database.AllProjectionProgress("acme", token)).ShouldBeEmpty();
        (await database.FetchProjectionProgressFor([theShard], token)).ShouldBeEmpty();
        (await database.ProjectionProgressFor(theShard, token)).ShouldBe(0);
        (await database.ReadProjectionProgressAsync("Fake", null, token)).ShouldBeNull();
        (await database.ReadProjectionProgressAsync(theShard, token)).ShouldBeNull();

        (await database.CountDeadLetterEventsAsync(theShard, token)).ShouldBe(0);
        (await database.FetchDeadLetterCountsAsync(token)).ShouldBeEmpty();
        (await database.FetchDeadLetterCountsAsync("acme", token)).ShouldBeEmpty();
        (await database.QueryDeadLetterEventsAsync(theShard, null, 0, 20, token)).ShouldBeEmpty();

        (await database.FetchHighestEventSequenceNumber(token)).ShouldBe(0);
        (await database.FetchMaxEventSequenceAsync(token)).ShouldBeNull();
        (await database.FindEventStoreFloorAtTimeAsync(DateTimeOffset.UtcNow, token)).ShouldBeNull();
        (await database.FindEventStoreFloorAtTimeAsync(DateTimeOffset.UtcNow, "acme", token)).ShouldBeNull();

        var statistics = await database.FetchEventStoreStatistics(token);
        statistics.EventCount.ShouldBe(0);
        statistics.StreamCount.ShouldBe(0);

        // The orphan-row eject path documents a non-existent identity as a clean no-op; a missing table is the
        // same answer for the same reason.
        await database.DeleteProjectionProgressByShardNameAsync(theShard.Identity, token);
    }

    /// <summary>
    /// The regression guard for the way #5510 first answered this. Inferring emptiness from
    /// <c>AutoCreate.None &amp;&amp; !IsActive</c> is a statement about the reader's configuration, not about the
    /// database, so it reported nothing for a diagnostics store -- exactly how you would configure an ops tool --
    /// pointed at a fully provisioned event store. Asking Postgres instead gets both cases right.
    /// </summary>
    [Fact]
    public async Task a_configuration_that_declares_no_events_still_reads_a_provisioned_event_store()
    {
        // A real store provisions the event tables in this schema and records progress.
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline));
        await theStore.Tenancy.Default.Database.EnsureStorageExistsAsync(typeof(IEvent));

        await using (var session = theStore.LightweightSession())
        {
            session.QueueOperation(
                new InsertProjectionProgress(theStore.Events, new EventRange(theShard, 42L)));
            await session.SaveChangesAsync();
        }

        // The monitor's store: same database and schema, no event types, no projections, AutoCreate.None.
        await using var monitor = SeparateStore(opts =>
        {
            opts.Schema.For<Target>();
            opts.AutoCreateSchemaObjects = AutoCreate.None;
        });

        var database = (MartenDatabase)monitor.Tenancy.Default.Database;

        (await database.ProjectionProgressFor(theShard, CancellationToken.None)).ShouldBe(42L);
        (await database.AllProjectionProgress(CancellationToken.None)).ShouldNotBeEmpty();
    }

    /// <summary>
    /// <see cref="Marten.Events.EventGraph.IsActive" /> is derived from a lazily-populated cache whose
    /// <c>OnMissing</c> registers the type it was asked for, so a guard reading it flips mid-process: the first
    /// event appended or read makes an inactive store active, and the next poll raised the very error the guard
    /// existed to prevent. The answer must not depend on what the store happened to touch first.
    /// </summary>
    [Fact]
    public async Task it_keeps_answering_nothing_after_an_event_type_registers()
    {
        var database = storeWithoutEvents();
        await leaveABareProgressionTableBehind();

        (await database.AllProjectionProgress(CancellationToken.None)).ShouldBeEmpty();

        // Whatever registers a type -- an append, a raw event query, this call -- takes the event store from
        // inactive to active without provisioning anything.
        theStore.Options.Events.AddEventType(typeof(AEvent));
        theStore.Options.EventGraph.IsActive(theStore.Options).ShouldBeTrue();

        (await database.AllProjectionProgress(CancellationToken.None)).ShouldBeEmpty();
        (await database.FetchDeadLetterCountsAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    public record AEvent(Guid Id);

    public class SimpleAggregate
    {
        public Guid Id { get; set; }
        public int Count { get; set; }

        public void Apply(AEvent _) => Count++;
    }
}
