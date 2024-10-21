using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using Marten;
using Marten.Events;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_1679_use_inner_type_for_stream_aggregation : AggregationContext
{
    public Bug_1679_use_inner_type_for_stream_aggregation(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task try_live_stream_aggregation_with_inner_class()
    {
        var stream = Guid.NewGuid();
        theSession.Events.StartStream(stream, new AEvent(), new AEvent(), new AEvent(), new BEvent(), new BEvent(),
            new CEvent());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<InnerAggregate>(stream);
        aggregate.ShouldNotBeNull();
    }

    [DocumentAlias("inner_aggregate")]
    public class InnerAggregate
    {
        public Guid Id { get; set; }

        public void Apply(EventA @event){}
        public void Apply(IEvent<EventB> @event){}

        public InnerAggregate Apply(IEvent<EventC> @event)
        {
            return this;
        }

        public Task<InnerAggregate> Apply(EventD @event, IQuerySession session)
        {
            return Task.FromResult(this);
        }
    }
}
