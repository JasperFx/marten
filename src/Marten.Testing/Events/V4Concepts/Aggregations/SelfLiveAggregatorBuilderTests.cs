using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.V4Concept.Aggregation;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Events.V4Concepts.Aggregations
{
    public class SelfLiveAggregatorBuilderTests
    {
        [Fact]
        public void try_existing_QuestParty()
        {
            var aggregator = SelfLiveAggregatingBuilder.Build<QuestParty>(new StoreOptions());
            aggregator.ShouldNotBeNull();
        }

        [Fact]
        public void try_with_all_possibilities()
        {
            SelfLiveAggregatingBuilder.Build<FakeAggregate>(new StoreOptions())
                .ShouldNotBeNull();
        }

        public class EventE{}

        public class FakeAggregate
        {
            public void Apply(EventA @event){}
            public void Apply(Event<EventB> @event){}

            public FakeAggregate Apply(Event<EventC> @event)
            {
                return this;
            }

            public Task<FakeAggregate> Apply(EventD @event, IQuerySession session)
            {
                return Task.FromResult(this);
            }

            public Task<FakeAggregate> Apply(Event<EventE> @event, IQuerySession session)
            {
                return Task.FromResult(this);
            }
        }
    }
}
