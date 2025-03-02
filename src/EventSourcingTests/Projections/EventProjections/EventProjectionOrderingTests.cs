using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Projections.EventProjections;

public class EventProjectionOrderingTests: IntegrationContext
{
    [Fact]
    public async Task ShouldRespectGlobalOrdering()
    {
        var firstStream = Guid.NewGuid();
        var secondStream = Guid.NewGuid();

        theSession.Events.Append(firstStream, new DummyEventForOrdering(1));
        await theSession.SaveChangesAsync();
        theSession.Events.Append(secondStream, new DummyEventForOrdering(2));
        await theSession.SaveChangesAsync();
        theSession.Events.Append(firstStream, new DummyEventForOrdering(3));
        await theSession.SaveChangesAsync();
        theSession.Events.Append(secondStream, new DummyEventForOrdering(4));
        await theSession.SaveChangesAsync();
        theSession.Events.Append(secondStream, new DummyEventForOrdering(5));
        await theSession.SaveChangesAsync();

        var daemon = await theStore.BuildProjectionDaemonAsync();

        await daemon.RebuildProjectionAsync<TestOrderingEventProjection>(CancellationToken.None);

        var results = theSession.Query<OrderingTracker>()
            .Where(x => x.StreamId == firstStream || x.StreamId == secondStream)
            .ToList();

        results.OrderBy(x => x.Sequence).ShouldHaveTheSameElementsAs(results.OrderBy(x => x.Order));
    }

    public EventProjectionOrderingTests(DefaultStoreFixture fixture): base(fixture)
    {
        StoreOptions(o =>
        {
            o.Projections.Add<TestOrderingEventProjection>(ProjectionLifecycle.Inline);
            o.Projections.DaemonLockId = 11127;
        });
    }
}

public class OrderingTracker
{
    public static int Counter;
    public Guid Id { get; init; }
    public Guid StreamId { get; init; }

    public long Sequence { get; init; }

    public int Order { get; init; }
}

public record DummyEventForOrdering(int Order);

public class TestOrderingEventProjection: EventProjection
{
    public OrderingTracker Transform(IEvent<DummyEventForOrdering> e)
    {
        return new OrderingTracker
        {
            Id = e.Id, StreamId = e.StreamId, Sequence = e.Sequence, Order = ++OrderingTracker.Counter
        };
    }
}
