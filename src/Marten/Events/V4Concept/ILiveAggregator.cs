using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Sessions;

namespace Marten.Events.V4Concept
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
