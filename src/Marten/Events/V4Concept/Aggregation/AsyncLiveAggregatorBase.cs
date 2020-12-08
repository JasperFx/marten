using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.V4Concept.Aggregation
{

    public abstract class AsyncLiveAggregatorBase<T> : ILiveAggregator<T> where T : class
    {
        public T Build(IReadOnlyList<IEvent> events, IQuerySession session)
        {
            return BuildAsync(events, session, CancellationToken.None).GetAwaiter().GetResult();
        }

        public abstract ValueTask<T> BuildAsync(IReadOnlyList<IEvent> events, IQuerySession session,
            CancellationToken cancellation);
    }
}
