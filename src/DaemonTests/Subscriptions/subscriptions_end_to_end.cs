using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using JasperFx.Core;
using JasperFx.Core.Descriptions;
using Lamar.IoC.Instances;
using Marten;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Daemon.Resiliency;
using Marten.Subscriptions;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NSubstitute;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.Subscriptions;

public class subscriptions_end_to_end: OneOffConfigurationsContext
{
    private readonly FakeSubscription theSubscription = new();

    public subscriptions_end_to_end()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Subscribe(theSubscription);
        });
    }

    [Fact]
    public async Task run_events_through()
    {
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var events1 = new object[] { new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events2 = new object[] { new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events3 = new object[] { new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events4 = new object[] { new EventSourcingTests.Aggregation.EEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };

        theSession.Events.StartStream(Guid.NewGuid(), events1);
        theSession.Events.StartStream(Guid.NewGuid(), events2);
        theSession.Events.StartStream(Guid.NewGuid(), events3);
        theSession.Events.StartStream(Guid.NewGuid(), events4);

        await theSession.SaveChangesAsync();

        await theStore.WaitForNonStaleProjectionDataAsync(20.Seconds());

        theSubscription.EventsEncountered.Count.ShouldBe(16);

        var progress = await theStore.Advanced.ProjectionProgressFor(new ShardName("Fake", "All"));
        progress.ShouldBe(16);
    }

    [Fact]
    public async Task start_subscription_at_sequence_floor()
    {
        // Just need it reset to nothing
        StoreOptions(opts => { });

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var events1 = new object[] { new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events2 = new object[] { new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events3 = new object[] { new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events4 = new object[] { new EventSourcingTests.Aggregation.EEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };

        theSession.Events.StartStream(Guid.NewGuid(), events1);
        theSession.Events.StartStream(Guid.NewGuid(), events2);
        theSession.Events.StartStream(Guid.NewGuid(), events3);
        theSession.Events.StartStream(Guid.NewGuid(), events4);

        await theSession.SaveChangesAsync();

        await theStore.WaitForNonStaleProjectionDataAsync(20.Seconds());

        var secondSubscription = new FakeSubscription();
        var secondStore = SeparateStore(opts =>
        {
            opts.Projections.Subscribe(secondSubscription, o => o.Options.SubscribeFromSequence(8));
        });

        await daemon.StopAllAsync();

        using var daemon2 = await secondStore.BuildProjectionDaemonAsync();
        await daemon2.StartAllAsync();
        await Task.Delay(2000); // yeah, I know
        await secondStore.WaitForNonStaleProjectionDataAsync(20.Seconds());

        secondSubscription.EventsEncountered.Count.ShouldBe(8);
    }

    [Fact]
    public async Task run_events_through_and_rewind_from_scratch()
    {
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var events1 = new object[] { new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events2 = new object[] { new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events3 = new object[] { new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events4 = new object[] { new EventSourcingTests.Aggregation.EEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };

        theSession.Events.StartStream(Guid.NewGuid(), events1);
        theSession.Events.StartStream(Guid.NewGuid(), events2);
        theSession.Events.StartStream(Guid.NewGuid(), events3);
        theSession.Events.StartStream(Guid.NewGuid(), events4);

        await theSession.SaveChangesAsync();

        await theStore.WaitForNonStaleProjectionDataAsync(20.Seconds());

        theSubscription.EventsEncountered.Clear();

        await daemon.RewindSubscriptionAsync("Fake", CancellationToken.None);

        await theStore.WaitForNonStaleProjectionDataAsync(30.Seconds());

        theSubscription.EventsEncountered.Count.ShouldBe(16);

        var progress = await theStore.Advanced.ProjectionProgressFor(new ShardName("Fake", "All"));
        progress.ShouldBe(16);
    }

    [Fact]
    public async Task run_events_through_and_rewind_from_middle()
    {
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var events1 = new object[] { new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events2 = new object[] { new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events3 = new object[] { new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events4 = new object[] { new EventSourcingTests.Aggregation.EEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };

        theSession.Events.StartStream(Guid.NewGuid(), events1);
        theSession.Events.StartStream(Guid.NewGuid(), events2);
        theSession.Events.StartStream(Guid.NewGuid(), events3);
        theSession.Events.StartStream(Guid.NewGuid(), events4);

        await theSession.SaveChangesAsync();

        await theStore.WaitForNonStaleProjectionDataAsync(20.Seconds());

        theSubscription.EventsEncountered.Clear();

        await daemon.RewindSubscriptionAsync("Fake", CancellationToken.None, 8);

        await theStore.WaitForNonStaleProjectionDataAsync(20.Seconds());

        theSubscription.EventsEncountered.Count.ShouldBe(8);

        var progress = await theStore.Advanced.ProjectionProgressFor(new ShardName("Fake", "All"));
        progress.ShouldBe(16);
    }

    [Fact]
    public async Task listener_registered_by_a_subscription_is_called()
    {
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var events1 = new object[] { new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events2 = new object[] { new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events3 = new object[] { new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events4 = new object[] { new EventSourcingTests.Aggregation.EEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };

        theSession.Events.StartStream(Guid.NewGuid(), events1);
        theSession.Events.StartStream(Guid.NewGuid(), events2);
        theSession.Events.StartStream(Guid.NewGuid(), events3);
        theSession.Events.StartStream(Guid.NewGuid(), events4);

        await theSession.SaveChangesAsync();

        await theStore.WaitForNonStaleProjectionDataAsync(20.Seconds());

        theSubscription.Listener.AfterCommitWasCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task run_events_through_with_event_filters()
    {
        theSubscription.IncludeType<EventSourcingTests.Aggregation.BEvent>();
        theSubscription.IncludeType<EventSourcingTests.Aggregation.EEvent>();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var events1 = new object[] { new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events2 = new object[] { new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events3 = new object[] { new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events4 = new object[] { new EventSourcingTests.Aggregation.EEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };

        theSession.Events.StartStream(Guid.NewGuid(), events1);
        theSession.Events.StartStream(Guid.NewGuid(), events2);
        theSession.Events.StartStream(Guid.NewGuid(), events3);
        theSession.Events.StartStream(Guid.NewGuid(), events4);

        await theSession.SaveChangesAsync();

        await theStore.WaitForNonStaleProjectionDataAsync(20.Seconds());

        theSubscription.EventsEncountered.Count.ShouldBe(5);
        theSubscription.EventsEncountered.All(x => x.Data is EventSourcingTests.Aggregation.BEvent or EventSourcingTests.Aggregation.EEvent).ShouldBeTrue();

        var progress = await theStore.Advanced.ProjectionProgressFor(new ShardName("Fake", "All"));
        progress.ShouldBe(16);
    }
}

public class using_simple_subscription_registrations: OneOffConfigurationsContext
{
    [Fact]
    public async Task use_end_to_end()
    {
        SimpleSubscription.Clear();

        StoreOptions(opts =>
        {
            opts.Projections.Subscribe(new SimpleSubscription(), x => x.SubscriptionName = "Simple");
        });

        theStore.Options.Projections.AllShards().Select(x => x.Name.Identity)
            .ShouldContain("Simple:All");

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var events1 = new object[] { new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events2 = new object[] { new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events3 = new object[] { new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events4 = new object[] { new EventSourcingTests.Aggregation.EEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };

        theSession.Events.StartStream(Guid.NewGuid(), events1);
        theSession.Events.StartStream(Guid.NewGuid(), events2);
        theSession.Events.StartStream(Guid.NewGuid(), events3);
        theSession.Events.StartStream(Guid.NewGuid(), events4);

        await theSession.SaveChangesAsync();

        await theStore.WaitForNonStaleProjectionDataAsync(20.Seconds());

        SimpleSubscription.EventsEncountered[1].Count.ShouldBe(16);

        var progress = await theStore.Advanced.ProjectionProgressFor(new ShardName("Simple", "All"));
        progress.ShouldBe(16);
    }

    [Fact]
    public async Task use_from_service_provider_as_singleton()
    {
        await rewindState();

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";
                }).AddAsyncDaemon(DaemonMode.Solo).AddSubscriptionWithServices<SimpleSubscription>(ServiceLifetime.Singleton,
                    o => o.SubscriptionName = "Simple2");
            }).StartAsync();

        var store = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();


        store.Options.Projections.AllShards().Select(x => x.Name.Identity)
            .ShouldContain("Simple2:All");

        var events1 = new object[] { new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events2 = new object[] { new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events3 = new object[] { new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events4 = new object[] { new EventSourcingTests.Aggregation.EEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };

        await using var session = store.LightweightSession();

        session.Events.StartStream(Guid.NewGuid(), events1);
        session.Events.StartStream(Guid.NewGuid(), events2);
        session.Events.StartStream(Guid.NewGuid(), events3);
        session.Events.StartStream(Guid.NewGuid(), events4);

        await session.SaveChangesAsync();

        await store.WaitForNonStaleProjectionDataAsync(60.Seconds());

        SimpleSubscription.EventsEncountered[1].Count.ShouldBeGreaterThanOrEqualTo(16);

        var progress = await store.Advanced.ProjectionProgressFor(new ShardName("Simple2", "All"));
        progress.ShouldBeGreaterThanOrEqualTo(16);

        await store.Advanced.Clean.DeleteAllEventDataAsync();
    }

    [Fact]
    public async Task use_from_service_provider_as_singleton_with_martenStore()
    {
        await rewindState();

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMartenStore<ICustomStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";
                }).AddAsyncDaemon(DaemonMode.Solo).AddSubscriptionWithServices<SimpleSubscription>(ServiceLifetime.Singleton,
                    o => o.SubscriptionName = "Simple2");
            }).StartAsync();

        var store = (DocumentStore)host.Services.GetRequiredService<ICustomStore>();


        store.Options.Projections.AllShards().Select(x => x.Name.Identity)
            .ShouldContain("Simple2:All");

        var events1 = new object[] { new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events2 = new object[] { new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events3 = new object[] { new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events4 = new object[] { new EventSourcingTests.Aggregation.EEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };

        await using var session = store.LightweightSession();

        session.Events.StartStream(Guid.NewGuid(), events1);
        session.Events.StartStream(Guid.NewGuid(), events2);
        session.Events.StartStream(Guid.NewGuid(), events3);
        session.Events.StartStream(Guid.NewGuid(), events4);

        await session.SaveChangesAsync();

        await store.WaitForNonStaleProjectionDataAsync(60.Seconds());

        SimpleSubscription.EventsEncountered[1].Count.ShouldBeGreaterThanOrEqualTo(16);

        var progress = await store.Advanced.ProjectionProgressFor(new ShardName("Simple2", "All"));
        progress.ShouldBeGreaterThanOrEqualTo(16);

        await store.Advanced.Clean.DeleteAllEventDataAsync();
    }

    private async Task rewindState()
    {
        SimpleSubscription.Clear();
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.DropSchemaAsync("ioc");
        await conn.CloseAsync();
    }

    [Fact]
    public async Task use_from_service_provider_as_scoped()
    {
        await rewindState();

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";
                }).AddAsyncDaemon(DaemonMode.Solo).AddSubscriptionWithServices<SimpleSubscription>(ServiceLifetime.Scoped,
                    o => o.SubscriptionName = "Simple2");
            }).StartAsync();

        var store = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();


        store.Options.Projections.AllShards().Select(x => x.Name.Identity)
            .ShouldContain("Simple2:All");

        var events1 = new object[] { new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events2 = new object[] { new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events3 = new object[] { new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events4 = new object[] { new EventSourcingTests.Aggregation.EEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };

        await using var session = store.LightweightSession();

        session.Events.StartStream(Guid.NewGuid(), events1);
        session.Events.StartStream(Guid.NewGuid(), events2);
        session.Events.StartStream(Guid.NewGuid(), events3);
        session.Events.StartStream(Guid.NewGuid(), events4);

        await session.SaveChangesAsync();

        await store.WaitForNonStaleProjectionDataAsync(60.Seconds());

        SimpleSubscription.EventsEncountered.Sum(x => x.Count).ShouldBeGreaterThanOrEqualTo(16);

        var progress = await store.Advanced.ProjectionProgressFor(new ShardName("Simple2", "All"));
        progress.ShouldBeGreaterThanOrEqualTo(16);

        await store.Advanced.Clean.DeleteAllEventDataAsync();
    }

    [Fact]
    public async Task subscriptions_are_part_of_the_event_store_usage()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMartenStore<ICustomStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";
                }).AddAsyncDaemon(DaemonMode.Solo).AddSubscriptionWithServices<SimpleSubscription>(ServiceLifetime.Scoped,
                    o => o.SubscriptionName = "Simple2");
            }).StartAsync();

        var capabilities = host.Services.GetServices<IEventStoreCapability>().ToArray();
        capabilities.Length.ShouldBe(1);

        var usage = await capabilities[0].TryCreateUsage(CancellationToken.None);

        usage.ShouldNotBeNull();
    }

    [Fact]
    public async Task use_from_service_provider_as_scoped_with_martenStore()
    {
        await rewindState();

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMartenStore<ICustomStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";
                }).AddAsyncDaemon(DaemonMode.Solo).AddSubscriptionWithServices<SimpleSubscription>(ServiceLifetime.Scoped,
                    o => o.SubscriptionName = "Simple2");
            }).StartAsync();

        var store = (DocumentStore)host.Services.GetRequiredService<ICustomStore>();


        store.Options.Projections.AllShards().Select(x => x.Name.Identity)
            .ShouldContain("Simple2:All");

        var events1 = new object[] { new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events2 = new object[] { new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events3 = new object[] { new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.AEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };
        var events4 = new object[] { new EventSourcingTests.Aggregation.EEvent(), new EventSourcingTests.Aggregation.BEvent(), new EventSourcingTests.Aggregation.DEvent(), new EventSourcingTests.Aggregation.CEvent() };

        await using var session = store.LightweightSession();

        session.Events.StartStream(Guid.NewGuid(), events1);
        session.Events.StartStream(Guid.NewGuid(), events2);
        session.Events.StartStream(Guid.NewGuid(), events3);
        session.Events.StartStream(Guid.NewGuid(), events4);

        await session.SaveChangesAsync();

        await store.WaitForNonStaleProjectionDataAsync(60.Seconds());

        SimpleSubscription.EventsEncountered.Sum(x => x.Count).ShouldBeGreaterThanOrEqualTo(16);

        var progress = await store.Advanced.ProjectionProgressFor(new ShardName("Simple2", "All"));
        progress.ShouldBeGreaterThanOrEqualTo(16);

        await store.Advanced.Clean.DeleteAllEventDataAsync();
    }

    [Fact]
    public void subscription_wrapper_copies_the_filters_from_subscription_base()
    {
        var services = new ServiceCollection();
        services.AddScoped<FilteredSubscription>();

        var provider = services.BuildServiceProvider();

        var wrapper = new ScopedSubscriptionServiceWrapper<FilteredSubscription>(provider);

        wrapper.IncludeArchivedEvents.ShouldBeTrue();
        wrapper.IncludedEventTypes.Count.ShouldBe(2);
        wrapper.IncludedEventTypes.ShouldContain(typeof(AEvent));
        wrapper.IncludedEventTypes.ShouldContain(typeof(BEvent));
    }
}

public class FilteredSubscription: SubscriptionBase, IDisposable
{
    public FilteredSubscription()
    {
        IncludeType<AEvent>();
        IncludeType<BEvent>();
        StreamType = typeof(SimpleAggregate);
        IncludeArchivedEvents = true;
    }

    public override Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller, IDocumentOperations operations,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Substitute.For<IChangeListener>());
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            // TODO release managed resources here
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

public class FilteredSubscription2: SubscriptionBase, IAsyncDisposable
{
    public FilteredSubscription2()
    {
        IncludeType<AEvent>();
        IncludeType<BEvent>();
        StreamType = typeof(SimpleAggregate);
        IncludeArchivedEvents = true;
    }

    public override Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller, IDocumentOperations operations,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Substitute.For<IChangeListener>());
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            // TODO release managed resources here
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

public class SimpleSubscription: ISubscription
{
    public static int InstanceCounter = 0;

    public static LightweightCache<int, List<IEvent>> EventsEncountered = new(i => new());

    public static void Clear()
    {
        InstanceCounter = 0;
        EventsEncountered.Clear();
    }

    public SimpleSubscription()
    {
        Instance = ++InstanceCounter;
    }

    public int Instance { get; set; }

    public ValueTask DisposeAsync()
    {
        return new ValueTask();
    }

    public Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller,
        IDocumentOperations operations,
        CancellationToken cancellationToken)
    {
        EventsEncountered[Instance].AddRange(page.Events);
        return Task.FromResult((IChangeListener)NullChangeListener.Instance);
    }
}

public interface ICustomStore: IDocumentStore
{
}
