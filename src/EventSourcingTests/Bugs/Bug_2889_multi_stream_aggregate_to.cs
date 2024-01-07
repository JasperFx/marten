using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Projections.MultiStreamProjections.CustomGroupers;
using Marten;
using Marten.Events;
using Marten.Events.Archiving;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Bugs;

public class Bug_2889_multi_stream_aggregate_to : BugIntegrationContext
{
    private readonly ITestOutputHelper _helper;

    public Bug_2889_multi_stream_aggregate_to(ITestOutputHelper helper)
    {
        _helper = helper;
        StoreOptions(x =>
        {
            x.Projections.Add<SimpleProjection>(ProjectionLifecycle.Inline);
        });
    }

    [Fact]
    public async Task CanAggregateWithMultiStream()
    {
        var idToMatch = "test";
        var number = 10;

        for (var i = 0; i < number; i++)
        {
            var stream = theSession.Events.StartStream(new SimpleEvent() { IdToMatch = idToMatch });
        }

        await theSession.SaveChangesAsync();
        var doc = theSession.Query<SimpleAggregate>().ToList();
        doc.Count.ShouldBe(1);
        doc.First().Count.ShouldBe(number);
        doc.First().Id.ShouldBe(idToMatch);

        var aggregate = theSession.Events.QueryAllRawEvents().AggregateTo<SimpleAggregate>();
        _helper.WriteLine(aggregate.ToString());
        aggregate.Id.ShouldBe(idToMatch);
        aggregate.Count.ShouldBe(number);

        aggregate = await theSession.Events.QueryAllRawEvents().AggregateToAsync<SimpleAggregate>();
        aggregate.Id.ShouldBe(idToMatch);
        aggregate.Count.ShouldBe(number);
    }


    public record SimpleAggregate
    {
        public string Id { get; set; }
        public int Count { get; set; } = 0;
    }

    public class SimpleProjection : MultiStreamProjection<SimpleAggregate, string>
    {
        public SimpleProjection()
        {
            Identity<SimpleEvent>(x => x.IdToMatch);
        }

        public SimpleAggregate Create(IEvent<SimpleEvent> @event) => new SimpleAggregate()
        {
            Count = 1,
        };

        public SimpleAggregate Apply(SimpleEvent simpleEvent, SimpleAggregate current)
            => current with
            {
                Count = current.Count + 1
            };

    }


    public class SimpleEvent
    {
        public string IdToMatch { get; set; }
    }
}
