using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Testing.Events.Aggregation;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Events.Bugs
{
    public class Bug_1679_use_inner_type_for_self_aggregate : AggregationContext
    {
        public Bug_1679_use_inner_type_for_self_aggregate(DefaultStoreFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task try_self_aggregate_with_inner_class()
        {
            var stream = Guid.NewGuid();
            theSession.Events.StartStream(stream, new AEvent(), new AEvent(), new AEvent(), new BEvent(), new BEvent(),
                new CEvent());
            await theSession.SaveChangesAsync();

            var aggregate = await theSession.Events.AggregateStreamAsync<InnerAggregate>(stream);
            aggregate.ShouldNotBeNull();
        }

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
}
