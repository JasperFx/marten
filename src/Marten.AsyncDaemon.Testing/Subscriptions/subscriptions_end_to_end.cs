using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing.Subscriptions;

public class subscriptions_end_to_end: OneOffConfigurationsContext
{
    private readonly FakeSubscription theSubscription = new();

    public subscriptions_end_to_end()
    {
        StoreOptions(opts => opts.Projections.Subscribe(theSubscription));
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
