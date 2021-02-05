using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Events.Aggregation
{
    public class SelfLiveAggregatorBuilderTests
    {
        [Fact]
        public void try_existing_QuestParty()
        {

            var aggregator = new AggregateProjection<QuestParty>().Build(new StoreOptions());
            aggregator.ShouldNotBeNull();
        }

        [Fact]
        public void try_with_all_possibilities()
        {
            new AggregateProjection<FakeAggregate>()
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

        public Task<FakeAggregate> Apply(IEvent<SelfLiveAggregatorBuilderTests.EventE> @event, IQuerySession session)
        {
            return Task.FromResult(this);
        }
    }
}
