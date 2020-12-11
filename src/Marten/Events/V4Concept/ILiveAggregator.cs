using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.V4Concept
{
    public interface ILiveAggregator<T>
    {
        T Build(IReadOnlyList<IEvent> events, IQuerySession session);
        ValueTask<T> BuildAsync(
            IReadOnlyList<IEvent> events,
            IQuerySession session,
            CancellationToken cancellation);
    }
}
