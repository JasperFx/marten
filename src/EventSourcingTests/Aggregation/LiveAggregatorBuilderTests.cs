using System;
using System.Threading.Tasks;
using EventSourcingTests.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class LiveAggregatorBuilderTests
{
    [Fact]
    public void try_existing_QuestParty()
    {
        var aggregator = new SingleStreamProjection<QuestParty>().Build(new StoreOptions());
        aggregator.ShouldNotBeNull();
    }

    [Fact]
    public void try_with_all_possibilities()
    {
        new SingleStreamProjection<FakeAggregate>()
            .Build(new StoreOptions())
            .ShouldNotBeNull();
    }

    public class EventE{}
}

public class FakeAggregate
{
    public Guid Id { get; set; }

    public void Apply(EventA @event){}
    public void Apply(IEvent<EventB> @event){}

    public FakeAggregate Apply(IEvent<EventC> @event)
    {
        return this;
    }

    public Task<FakeAggregate> Apply(EventD @event, IQuerySession session)
    {
        return Task.FromResult(this);
    }

    public Task<FakeAggregate> Apply(IEvent<LiveAggregatorBuilderTests.EventE> @event, IQuerySession session)
    {
        return Task.FromResult(this);
    }
}
