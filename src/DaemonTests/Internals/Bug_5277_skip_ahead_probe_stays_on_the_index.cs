using System;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.MultiTenancy;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Archiving;
using Marten.Events.Daemon.Internals;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.SqlGeneration;
using Xunit;

namespace DaemonTests.Internals;

/// <summary>
/// #5277 — the skip-ahead probe asked for <c>min(d.seq_id)</c> over a join to <c>mt_streams</c>.
/// Postgres only rewrites <c>MIN</c> into an ordered index scan when the aggregate's input is a
/// single relation, so the joined form planned as a bitmap heap scan of every remaining row in the
/// partition: on a 1M-event store, 77ms and 27k buffers where the index scan needs 0.9ms and 536.
/// That is O(events after the floor) on the one code path that exists BECAUSE the store is too
/// large for the normal query to finish.
///
/// <para>
/// Two changes: the probe orders by seq_id and takes one row instead of aggregating, which keeps
/// the index scan even when the join IS required; and it only joins <c>mt_streams</c> when a filter
/// actually references the <c>s</c> alias, which today means an <see cref="AggregateTypeFilter" />
/// and nothing else.
/// </para>
/// </summary>
public class Bug_5277_skip_ahead_probe_sql_shape
{
    private static string UniqueSchema() =>
        $"bug5277_{Guid.NewGuid().ToString("N")[..16]}_{Environment.ProcessId}";

    // Pure SQL-shape assertions -- EventLoader builds its commands from StoreOptions with no
    // database round-trip, so the store is never applied.
    private static EventLoader BuildLoader(ISqlFragment[] filters)
    {
        var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = UniqueSchema();
        });

        var db = (MartenDatabase)store.Storage.Database;
        return new EventLoader(store, db, new AsyncOptions(), filters);
    }

    [Fact]
    public void probe_takes_the_first_row_in_seq_id_order_rather_than_aggregating()
    {
        var loader = BuildLoader(Array.Empty<ISqlFragment>());

        loader.SkipAheadCommandText.ShouldNotContain("min(", Case.Insensitive,
            "MIN over the filtered set cannot be served by an ordered index scan");
        loader.SkipAheadCommandText.ShouldContain("order by d.seq_id limit 1", Case.Insensitive);
    }

    [Fact]
    public void probe_does_not_join_streams_when_no_filter_references_them()
    {
        var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = UniqueSchema();
        });

        // The reported shape: d.type = ANY(...) and d.is_archived = FALSE, nothing touching s
        var loader = new EventLoader(store, (MartenDatabase)store.Storage.Database, new AsyncOptions(),
            [new EventTypeFilter(store.Events, [typeof(MTAEvent)]), IsNotArchivedFilter.Instance]);

        loader.SkipAheadCommandText.ShouldNotContain("mt_streams", Case.Insensitive,
            "mt_events.stream_id is a foreign key into mt_streams, so the join matches every row and only costs work");
    }

    [Fact]
    public void probe_still_joins_streams_when_a_filter_references_them()
    {
        var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = UniqueSchema();
        });

        var loader = new EventLoader(store, (MartenDatabase)store.Storage.Database, new AsyncOptions(),
            [new AggregateTypeFilter(typeof(Letters), store.Events)]);

        // #4744: without the join, "s.type = ?" references a missing FROM-clause entry (42P01).
        loader.SkipAheadCommandText.ShouldContain("mt_streams", Case.Insensitive);
        loader.SkipAheadCommandText.ShouldContain("order by d.seq_id limit 1", Case.Insensitive);
    }
}

public class Bug_5277_skip_ahead_probe_behaviour: OneOffConfigurationsContext
{
    // The join-free probe has to find the same events the joined one did.
    [Fact]
    public async Task skip_ahead_probe_finds_events_without_the_streams_join()
    {
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<Letters>(streamId, new MTAEvent(), new MTBEvent(), new MTCEvent());
        await theSession.SaveChangesAsync();

        // Only a d-side filter, so the probe drops the join entirely
        var filters = new ISqlFragment[]
        {
            new EventTypeFilter(theStore.Events, [typeof(MTAEvent), typeof(MTBEvent), typeof(MTCEvent)])
        };

        var loader = new EventLoader(theStore, (MartenDatabase)theStore.Tenancy.Default.Database,
            new AsyncOptions(), filters);

        loader.SkipAheadCommandText.ShouldNotContain("mt_streams");

        var page = await loader.LoadWithSkipAheadAsync(Request(), CancellationToken.None);

        page.Count.ShouldBe(3);
    }

    // No matching event at all: MIN() answered with one NULL row, LIMIT 1 answers with no row.
    [Fact]
    public async Task skip_ahead_probe_returns_an_empty_page_when_nothing_matches()
    {
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        theSession.Events.StartStream<Letters>(Guid.NewGuid(), new MTAEvent());
        await theSession.SaveChangesAsync();

        var filters = new ISqlFragment[]
        {
            // Nothing in the store carries this type
            new EventTypeFilter(theStore.Events, [typeof(MTCEvent)])
        };

        var loader = new EventLoader(theStore, (MartenDatabase)theStore.Tenancy.Default.Database,
            new AsyncOptions(), filters);

        var page = await loader.LoadWithSkipAheadAsync(Request(), CancellationToken.None);

        page.Count.ShouldBe(0);
    }

    private static EventRequest Request() => new()
    {
        Floor = 0,
        HighWater = 1000,
        BatchSize = 1000,
        ErrorOptions = new ErrorHandlingOptions(),
        Runtime = new NulloDaemonRuntime(),
        Name = new ShardName("Letters", "All", 1)
    };
}
