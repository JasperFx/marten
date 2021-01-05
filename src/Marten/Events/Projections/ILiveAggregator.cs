using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections
{
    public interface ILiveAggregator<T>
    {
        T Build(IReadOnlyList<IEvent> events, IQuerySession session, T snapshot);
        ValueTask<T> BuildAsync(
            IReadOnlyList<IEvent> events,
            IQuerySession session,
            T snapshot,
            CancellationToken cancellation);

    }
}
