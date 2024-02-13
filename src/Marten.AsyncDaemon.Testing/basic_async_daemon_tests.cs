using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Linq.SqlGeneration;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using NSubstitute;
using Shouldly;
using Weasel.Postgresql.SqlGeneration;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing;

public class basic_async_daemon_tests: DaemonContext
{
    private readonly ISubscriptionAgent theAgent;

    public basic_async_daemon_tests(ITestOutputHelper output): base(output)
    {
        theAgent = Substitute.For<ISubscriptionAgent>();
        theAgent.Mode.Returns(ShardExecutionMode.Continuous);
    }

    [Fact]
    public async Task start_stop_and_restart_a_new_daemon()
    {
        StoreOptions(x => x.Projections.Add(new TripProjectionWithCustomName(), ProjectionLifecycle.Async));

        using var daemon = await StartDaemon();
        await daemon.StartAllAsync();

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        await daemon.Tracker.WaitForHighWaterMark(NumberOfEvents);

        await daemon.StopAllAsync();


        using var daemon2 = await StartDaemon();
        await daemon2.Tracker.WaitForHighWaterMark(NumberOfEvents);

        await daemon2.StartAllAsync();
    }

    #region sample_AsyncDaemonListener

    public class FakeListener: IChangeListener
    {
        public List<IChangeSet> Befores = new();
        public IList<IChangeSet> Changes = new List<IChangeSet>();

        public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
        {
            session.ShouldNotBeNull();
            Changes.Add(commit);
            return Task.CompletedTask;
        }

        public Task BeforeCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
        {
            session.ShouldNotBeNull();
            Befores.Add(commit);

            Changes.Count.ShouldBeLessThan(Befores.Count);

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
        await daemon.StartAllAsync();

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        await daemon.Tracker.WaitForShardState("TripCustomName:All", NumberOfEvents);

        await daemon.StopAllAsync();

        listener.Befores.Any().ShouldBeTrue();
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
        await daemon.StartAllAsync();

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        await daemon.Tracker.WaitForShardState("TripCustomName:All", NumberOfEvents);

        await daemon.StopAllAsync();

        listener.Changes.Clear(); // clear state before doing this again

        await daemon.RebuildProjectionAsync<TripProjectionWithCustomName>(CancellationToken.None);

        listener.Changes.Any().ShouldBeFalse();
    }

    [Fact]
    public async Task start_and_stop_a_projection()
    {
        StoreOptions(x => x.Projections.Add(new TripProjectionWithCustomName(), ProjectionLifecycle.Async));

        using var daemon = await StartDaemon();
        await daemon.StartAllAsync();

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        await daemon.Tracker.WaitForHighWaterMark(NumberOfEvents);

        await daemon.StopAgentAsync("Trip:All");

        daemon.StatusFor("Trip:All")
            .ShouldBe(AgentStatus.Stopped);
    }

    [Fact]
    public async Task event_fetcher_simple_case()
    {
        var fetcher =
            new EventLoader(theStore, (MartenDatabase)theStore.Tenancy.Default.Database, new AsyncOptions(), new ISqlFragment[0]);

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        var shardName = new ShardName("name");

        var range1 = await fetcher.LoadAsync(new EventRequest{Floor = 0, BatchSize = 10, HighWater = 10, Name = shardName, Runtime = new NulloDaemonRuntime()}, CancellationToken.None);

        var range2 = await fetcher.LoadAsync(new EventRequest{Floor = 10, BatchSize = 10, HighWater = 20, Name = shardName, Runtime = new NulloDaemonRuntime()}, CancellationToken.None);
        var range3 = await fetcher.LoadAsync(new EventRequest{Floor = 20, BatchSize = 10, HighWater = 38, Name = shardName, Runtime = new NulloDaemonRuntime()}, CancellationToken.None);

        range1.Count.ShouldBe(10);
        range2.Count.ShouldBe(10);
        range3.Count.ShouldBe(18);
    }

    [Fact]
    public async Task publish_single_file()
    {
        NumberOfStreams = 10;
        await PublishSingleThreaded();

        var statistics = await theStore.Advanced.FetchEventStoreStatistics();

        statistics.EventCount.ShouldBe(NumberOfEvents);
        statistics.StreamCount.ShouldBe(NumberOfStreams);
    }


    [Fact]
    public async Task publish_multi_threaded()
    {
        NumberOfStreams = 100;
        await PublishMultiThreaded(10);

        var statistics = await theStore.Advanced.FetchEventStoreStatistics();

        statistics.EventCount.ShouldBe(NumberOfEvents);
        statistics.StreamCount.ShouldBe(NumberOfStreams);
    }
}
