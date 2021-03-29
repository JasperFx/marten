using System.Linq;
using Marten.Linq;

namespace Marten.Events
{
    public static class EventStoreExtensions
    {
        public static T AggregateTo<T>(this IMartenQueryable<IEvent> queryable) where T : class
        {
            var session = ((MartenLinqQueryable<IEvent>)queryable).MartenSession;
            var aggregator = session.Options.Events.Projections.AggregatorFor<T>();

            var aggregate = aggregator.Build(queryable.ToList(), (IQuerySession)session, null);

            return aggregate;
        }
    }
}
