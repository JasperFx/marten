using System.Linq;
using Baseline;
using Marten.Linq;

namespace Marten.Events
{
    public static class EventStoreExtensions
    {
        public static T AggregateTo<T>(this IMartenQueryable<IEvent> queryable) where T : class
        {
            var session = queryable.As<MartenLinqQueryable<IEvent>>().MartenSession;
            var aggregator = session.Options.Events.Projections.AggregatorFor<T>();

            var aggregate = aggregator.Build(queryable.ToList(), (IQuerySession)session, null);

            return aggregate;
        }
    }
}
