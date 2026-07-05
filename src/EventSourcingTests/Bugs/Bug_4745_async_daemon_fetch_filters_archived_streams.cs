using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Daemon.Internals;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.SqlGeneration;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #4745 — the async daemon event fetch joined <c>mt_events</c> to
/// <c>mt_streams</c> but only filtered the event row's <c>is_archived</c>, never
/// the stream's. With <c>UseArchivedStreamPartitioning = true</c> the planner
/// therefore could not partition-prune the archived <c>mt_streams</c> partition,
/// and an archived stream's events could still surface if an event row's flag
/// drifted out of sync with its stream. The loader now appends
/// <c>and s.is_archived = FALSE</c> to the stream join whenever partitioning is on
/// unless the projection explicitly includes archived events.
///
/// <para>
/// These are pure SQL-shape assertions — <see cref="EventLoader"/> builds its
/// command from <see cref="StoreOptions"/> with no database round-trip, so the
/// store is never applied.
/// </para>
/// </summary>
public class Bug_4745_async_daemon_fetch_filters_archived_streams
{
    private static string UniqueSchema() =>
        $"bug4745_{Guid.NewGuid().ToString("N")[..16]}_{Environment.ProcessId}";

    private static EventLoader BuildLoader(bool usePartitioning, bool includeArchivedEvents = false)
    {
        var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = UniqueSchema();
            o.Events.UseArchivedStreamPartitioning = usePartitioning;
        });

        var db = (MartenDatabase)store.Storage.Database;
        return new EventLoader(store, db, new AsyncOptions(), Array.Empty<ISqlFragment>(), includeArchivedEvents);
    }

    [Fact]
    public void fetch_sql_constrains_stream_is_archived_when_partitioning_is_on()
    {
        var loader = BuildLoader(usePartitioning: true);

        loader.CommandText.ShouldContain("s.is_archived = FALSE",
            Case.Insensitive,
            "the join to mt_streams must exclude archived streams so the archived partition can be pruned");
    }

    [Fact]
    public void fetch_sql_does_not_constrain_stream_is_archived_when_including_archived_events()
    {
        var loader = BuildLoader(usePartitioning: true, includeArchivedEvents: true);

        loader.CommandText.ShouldNotContain("s.is_archived");
    }

    [Fact]
    public void fetch_sql_does_not_constrain_stream_is_archived_when_partitioning_is_off()
    {
        var loader = BuildLoader(usePartitioning: false);

        // Without partitioning, mt_streams is not partitioned by is_archived, so the
        // stream-level predicate adds no value and could miss an index.
        loader.CommandText.ShouldNotContain("s.is_archived");
    }
}

public class Bug_4745_include_archived_events_with_archived_stream_partitioning:
    OneOffConfigurationsContext, IAsyncLifetime
{
    public Bug_4745_include_archived_events_with_archived_stream_partitioning()
    {
        StoreOptions(opts => opts.Events.UseArchivedStreamPartitioning = true);
    }

    public async Task InitializeAsync()
    {
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task event_loader_fetches_events_from_archived_stream_when_including_archived_events()
    {
        var stream = Guid.NewGuid();
        theSession.Events.StartStream(stream, new AEvent(), new BEvent(), new CEvent());
        await theSession.SaveChangesAsync();

        theSession.Events.ArchiveStream(stream);
        await theSession.SaveChangesAsync();

        var fetcher = new EventLoader(theStore, (MartenDatabase)theStore.Tenancy.Default.Database, new AsyncOptions(),
            Array.Empty<ISqlFragment>(), includeArchivedEvents: true);

        var results = await fetcher.LoadAsync(
            new EventRequest
            {
                Floor = 0,
                BatchSize = 1000,
                HighWater = long.MaxValue,
                ErrorOptions = new ErrorHandlingOptions(),
                Runtime = new NulloDaemonRuntime(),
                Name = new ShardName("bug-4745", "All", 1)
            },
            CancellationToken.None);

        results.Count.ShouldBe(3);
        results.All(x => x.IsArchived).ShouldBeTrue();
    }
}
