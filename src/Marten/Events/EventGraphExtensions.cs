using Marten.Events.Projections;
using Marten.Services.Events;
using Baseline;

namespace Marten.Events
{
    public static class EventGraphExtensions
    {
        public static EventGraph UseAggregatorLookup(this EventGraph eventGraph, AggregationLookupStrategy strategy)
        {
            if (strategy == AggregationLookupStrategy.UsePublicApply)
            {
                eventGraph.UseAggregatorLookup(new AggregatorLookup(type => typeof(Aggregator<>).CloseAndBuildAs<IAggregator>(type)));
            }
            else if (strategy == AggregationLookupStrategy.UsePrivateApply)
            {
                eventGraph.UseAggregatorLookup(new AggregatorLookup(type => typeof(AggregatorApplyPrivate<>).CloseAndBuildAs<IAggregator>(type)));
            }

            return eventGraph;
        }
    }
}