using System;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.MultiTenancy;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Internals;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.SqlGeneration;
using Xunit;

namespace DaemonTests.Internals;

public class Bug_4744_skip_ahead_min_query_joins_streams: OneOffConfigurationsContext
{
    // #4744: a projection that filters incoming events on stream type contributes an
    // AggregateTypeFilter, which emits "s.type = ?". When the normal fetch times out on a
    // high-volume store the loader escalates to the skip-ahead MIN(seq_id) probe — but that
    // probe built its SQL from mt_events ALONE, with no join to mt_streams, so the s.type
    // predicate referenced a missing FROM-clause entry and Postgres threw 42P01
    // ("missing FROM-clause entry for table s"). Drive the probe directly (no need to provoke a
    // real timeout) and prove it both runs and returns the matching events.
    [Fact]
    public async Task skip_ahead_probe_joins_streams_when_filtering_on_aggregate_type()
    {
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<Letters>(streamId, new MTAEvent(), new MTBEvent(), new MTCEvent());
        await theSession.SaveChangesAsync();

        var filters = new ISqlFragment[]
        {
            // Mirrors the reported SQL shape: d.type = ANY(...) AND s.type = ?
            new EventTypeFilter(theStore.Events, new[] { typeof(MTAEvent), typeof(MTBEvent), typeof(MTCEvent) }),
            new AggregateTypeFilter(typeof(Letters), theStore.Events)
        };

        var loader = new EventLoader(theStore, (MartenDatabase)theStore.Tenancy.Default.Database,
            new AsyncOptions(), filters);

        var request = new EventRequest
        {
            Floor = 0,
            HighWater = 1000,
            BatchSize = 1000,
            ErrorOptions = new ErrorHandlingOptions(),
            Runtime = new NulloDaemonRuntime(),
            Name = new ShardName("Letters", "All", 1)
        };

        // Pre-fix this throws PostgresException 42P01 from the malformed MIN(seq_id) probe.
        var page = await loader.LoadWithSkipAheadAsync(request, CancellationToken.None);

        page.Count.ShouldBe(3);
    }
}
