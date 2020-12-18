using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.V4Concept.Aggregation
{
    public abstract class SyncLiveAggregatorBase<T> : ILiveAggregator<T> where T : class
    {
        public abstract T Build(IReadOnlyList<IEvent> events, IQuerySession session, T snapshot);

        public ValueTask<T> BuildAsync(IReadOnlyList<IEvent> events, IQuerySession session, T snapshot, CancellationToken cancellation)
        {
            return new(Build(events, session, snapshot));
        }
    }
}
