using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Storage;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_2296_tenant_session_in_grouper : OneOffConfigurationsContext
{
    [Fact]
    public async Task CanQueryTenantedStreamsInAsyncProjectionGrouper()
    {
        StoreOptions(_ =>
        {
            _.Policies.AllDocumentsAreMultiTenanted();
            _.Events.TenancyStyle = TenancyStyle.Conjoined;
            _.Events.StreamIdentity = StreamIdentity.AsString;
            _.Projections.Add<CountsByTagProjector>(ProjectionLifecycle.Async);
        });

        const string tenant = "myTenant";
        await using var tenantedSession = theStore.LightweightSession(tenant);

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllShards();

        var streamKey = CombGuidIdGeneration.NewGuid().ToString();
        tenantedSession.Events.StartStream(streamKey,
            new CountEvent {Tag = "Foo"},
            new CountEvent {Tag = "Bar"},
            new CountEvent {Tag = "Bar"},
            new CountEvent {Tag = "Baz"},
            new CountEvent {Tag = "Baz"},
            new CountEvent {Tag = "Baz"});
        await tenantedSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(5.Seconds());

        var counts1 = await tenantedSession.Query<CountsByTag>().ToListAsync();

        Assert.Equal(1, counts1.First(c => c.Tag == "Foo").Count);
        Assert.Equal(2, counts1.First(c => c.Tag == "Bar").Count);
        Assert.Equal(3, counts1.First(c => c.Tag == "Baz").Count);

        tenantedSession.Events.Append(streamKey, new ResetEvent());
        await tenantedSession.SaveChangesAsync();

        await daemon.WaitForNonStaleData(5.Seconds());

        var counts2 = await tenantedSession.Query<CountsByTag>().ToListAsync();

        Assert.Equal(0, counts2.First(c => c.Tag == "Foo").Count);
        Assert.Equal(0, counts2.First(c => c.Tag == "Bar").Count);
        Assert.Equal(0, counts2.First(c => c.Tag == "Baz").Count);
    }

    public class CountEvent
    {
        public string Tag { get; set; }
    }

    public class ResetEvent
    {
    }

    public class CountsByTag
    {
        [Identity]
        public string Tag { get; set; }
        public int Count { get; set; } = 0;
    }

    public class CountsByTagProjector: MultiStreamAggregation<CountsByTag, string>
    {
        public CountsByTagProjector()
        {
            Identity<CountEvent>(e => e.Tag);
            CustomGrouping(new EventGrouper());
        }

        public void Apply(CountEvent @event, CountsByTag view)
        {
            view.Count++;
        }

        public void Apply(ResetEvent @event, CountsByTag view)
        {
            view.Count = 0;
        }

        public class EventGrouper: IAggregateGrouper<string>
        {
            public async Task Group(IQuerySession session, IEnumerable<IEvent> events, ITenantSliceGroup<string> grouping)
            {
                var resetEvents = events.OfType<IEvent<ResetEvent>>().ToList();
                if (!resetEvents.Any())
                {
                    return;
                }

                foreach (var resetEvent in resetEvents)
                {
                    // DEBUG HERE
                    // check session.TenantId and session.Events._tenant.TenantId
                    // returns empty collection, should return all events in stream.
                    var streamEvents =
                        await session.Events.FetchStreamAsync(resetEvent.StreamKey!, version: resetEvent.Version);

                    foreach (var tag in streamEvents.OfType<IEvent<CountEvent>>().GroupBy(foo => foo.Data.Tag).Select(g => g.Key))
                    {
                        grouping.AddEvent(tag, resetEvent);
                    }
                }
            }
        }
    }
}
