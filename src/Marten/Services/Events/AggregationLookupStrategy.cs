using Baseline;
using Marten.Events.Projections;

namespace Marten.Services.Events
{
    public static class AggregationLookupStrategy
    {
        public static IAggregatorLookup UsePublicApply
            => new AggregatorLookup(type => typeof(Aggregator<>).CloseAndBuildAs<IAggregator>(type));

        public static IAggregatorLookup UsePrivateApply
            => new AggregatorLookup(type => typeof(AggregatorApplyPrivate<>).CloseAndBuildAs<IAggregator>(type));

        public static IAggregatorLookup UsePublicAndPrivateApply
            => new AggregatorLookup(type => typeof(AggregatorApplyPublicAndPrivate<>).CloseAndBuildAs<IAggregator>(type));
    }
}