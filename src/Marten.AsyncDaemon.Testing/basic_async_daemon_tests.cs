using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Linq.SqlGeneration;
using Marten.Services;
using Marten.Testing.Harness;
using NSubstitute;
using Shouldly;
using Weasel.Postgresql.SqlGeneration;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing;

public class basic_async_daemon_tests: DaemonContext
{
    private readonly IShardAgent theAgent;

    public basic_async_daemon_tests(ITestOutputHelper output): base(output)
    {
        theAgent = Substitute.For<IShardAgent>();
        theAgent.Mode.Returns(ShardExecutionMode.Continuous);
    }

    [Fact]
    public async Task start_stop_and_restart_a_new_daemon()
    {
        StoreOptions(x => x.Projections.Add(new TripProjectionWithCustomName(), ProjectionLifecycle.Async));

        using var daemon = await StartDaemon();
        await daemon.StartAllShards();

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        await daemon.Tracker.WaitForHighWaterMark(NumberOfEvents);

        await daemon.StopAll();


        using var daemon2 = await StartDaemon();
        await daemon2.Tracker.WaitForHighWaterMark(NumberOfEvents);

        await daemon2.StartAllShards();
    }

    #region sample_AsyncDaemonListener

    public class FakeListener: IChangeListener
    {
        public IList<IChangeSet> Changes = new List<IChangeSet>();

        public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
        {
            session.ShouldNotBeNull();
            Changes.Add(commit);
            return Task.CompletedTask;
        }
    }

    #endregion

    [Fact]
    public async Task can_listen_for_commits_in_daemon()
    {
        #region sample_AsyncListeners

        var listener = new FakeListener();
        StoreOptions(x =>
        {
            x.Projections.Add(new TripProjectionWithCustomName(), ProjectionLifecycle.Async);
            x.Projections.AsyncListeners.Add(listener);
        });

        #endregion

        using var daemon = await StartDaemon();
        await daemon.StartAllShards();

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        await daemon.Tracker.WaitForShardState("TripCustomName:All", NumberOfEvents);

        await daemon.StopAll();

        listener.Changes.Any().ShouldBeTrue();
    }

    [Fact]
    public async Task listeners_are_not_active_in_rebuilds()
    {
        var listener = new FakeListener();
        StoreOptions(x =>
        {
            x.Projections.Add(new TripProjectionWithCustomName(), ProjectionLifecycle.Async);
            x.Projections.AsyncListeners.Add(listener);
        });

        using var daemon = await StartDaemon();
        await daemon.StartAllShards();

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        await daemon.Tracker.WaitForShardState("TripCustomName:All", NumberOfEvents);

        await daemon.StopAll();

        listener.Changes.Clear(); // clear state before doing this again

        await daemon.RebuildProjection<TripProjectionWithCustomName>(CancellationToken.None);

        listener.Changes.Any().ShouldBeFalse();
    }

    [Fact]
    public async Task start_and_stop_a_projection()
    {
        StoreOptions(x => x.Projections.Add(new TripProjectionWithCustomName(), ProjectionLifecycle.Async));

        using var daemon = await StartDaemon();
        await daemon.StartAllShards();

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        await daemon.Tracker.WaitForHighWaterMark(NumberOfEvents);

        await daemon.StopShard("Trip:All");

        daemon.StatusFor("Trip:All")
            .ShouldBe(AgentStatus.Stopped);
    }

    [Fact]
    public async Task event_fetcher_simple_case()
    {
        using var fetcher =
            new EventFetcher(TheStore, theAgent, TheStore.Tenancy.Default.Database, new ISqlFragment[0]);

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        var shardName = new ShardName("name");
        var range1 = new EventRange(shardName, 0, 10);


        await fetcher.Load(range1, CancellationToken.None);

        var range2 = new EventRange(shardName, 10, 20);
        await fetcher.Load(range2, CancellationToken.None);

        var range3 = new EventRange(shardName, 20, 38);
        await fetcher.Load(range3, CancellationToken.None);

        range1.Events.Count.ShouldBe(10);
        range2.Events.Count.ShouldBe(10);
        range3.Events.Count.ShouldBe(18);
    }

    [Fact]
    public async Task use_type_filters()
    {
        NumberOfStreams = 10;
        await PublishSingleThreaded();

        using var fetcher1 =
            new EventFetcher(TheStore, theAgent, TheStore.Tenancy.Default.Database, new ISqlFragment[0]);

        var shardName = new ShardName("name");
        var range1 = new EventRange(shardName, 0, NumberOfEvents);
        await fetcher1.Load(range1, CancellationToken.None);

        var uniqueTypeCount = range1.Events.Select(x => x.EventType).Distinct()
            .Count();

        uniqueTypeCount.ShouldBe(6);

        var filter = new EventTypeFilter(TheStore.Events, new Type[] { typeof(Travel), typeof(Arrival) });
        using var fetcher2 = new EventFetcher(TheStore, theAgent, TheStore.Tenancy.Default.Database,
            new ISqlFragment[] { filter });

        var range2 = new EventRange(shardName, 0, NumberOfEvents);
        await fetcher2.Load(range2, CancellationToken.None);
        range2.Events
            .Select(x => x.EventType)
            .OrderBy(x => x.Name).Distinct()
            .ShouldHaveTheSameElementsAs(typeof(Arrival), typeof(Travel));
    }

    [Fact]
    public async Task publish_single_file()
    {
        NumberOfStreams = 10;
        await PublishSingleThreaded();

        var statistics = await TheStore.Advanced.FetchEventStoreStatistics();

        statistics.EventCount.ShouldBe(NumberOfEvents);
        statistics.StreamCount.ShouldBe(NumberOfStreams);
    }


    [Fact]
    public async Task publish_multi_threaded()
    {
        NumberOfStreams = 100;
        await PublishMultiThreaded(10);

        var statistics = await TheStore.Advanced.FetchEventStoreStatistics();

        statistics.EventCount.ShouldBe(NumberOfEvents);
        statistics.StreamCount.ShouldBe(NumberOfStreams);
    }
}
