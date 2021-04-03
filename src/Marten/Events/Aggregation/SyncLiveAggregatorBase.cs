using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
#nullable enable
namespace Marten.Events.Aggregation
{
    public abstract class SyncLiveAggregatorBase<T> : ILiveAggregator<T> where T : class
    {
        public abstract T Build(IReadOnlyList<IEvent> events, IQuerySession session, T? snapshot);

        public ValueTask<T> BuildAsync(IReadOnlyList<IEvent> events, IQuerySession session, T? snapshot, CancellationToken cancellation)
        {
            return new(Build(events, session, snapshot));
        }
    }
}
