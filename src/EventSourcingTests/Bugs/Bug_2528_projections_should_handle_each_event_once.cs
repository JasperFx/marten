using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_2528_projections_should_handle_each_event_once: BugIntegrationContext
{
    [Fact]
    public async Task count_of_events_should_match_event_count()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new IncrementEvent(), new IncrementEvent(), new IncrementEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<CounterProjection>(streamId);

        Assert.NotNull(aggregate);
        Assert.Equal(3, aggregate.Counter);
    }

    public record IncrementEvent;

    public record CounterProjection(Guid Id, int Counter)
    {
        public static CounterProjection Create(IncrementEvent @event, IEvent metadata)
        {
            return new CounterProjection(metadata.StreamId, 1);
        }

        public CounterProjection Apply(IncrementEvent @event, CounterProjection current)
        {
            return current with {Counter = current.Counter + 1};
        }
    }
}
