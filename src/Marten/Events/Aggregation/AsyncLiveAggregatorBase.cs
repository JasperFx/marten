using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
#nullable enable
namespace Marten.Events.Aggregation
{

    public abstract class AsyncLiveAggregatorBase<T> : ILiveAggregator<T> where T : class
    {
        public abstract ValueTask<T> BuildAsync(IReadOnlyList<IEvent> events, IQuerySession session, T? snapshot,
            CancellationToken cancellation);

        public T Build(IReadOnlyList<IEvent> events, IQuerySession session, T? snapshot)
        {
            return BuildAsync(events, session, snapshot, CancellationToken.None).GetAwaiter().GetResult();
        }

    }
}
