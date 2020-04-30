using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class AggregationProjectionTests
    {
        [Fact]
        public void consumes_delegates_to_internal_aggregation()
        {
            var aggregator = new Aggregator<QuestParty>();
            var projection = new AggregationProjection<QuestParty>(null, aggregator);

            projection.Consumes.ShouldHaveTheSameElementsAs(aggregator.EventTypes);
        }

        [Fact]
        public void produces_the_view_type()
        {
            var aggregator = new Aggregator<QuestParty>();
            var projection = new AggregationProjection<QuestParty>(null, aggregator);

            projection.Produces.ShouldBe(typeof(QuestParty));
        }
    }
}
