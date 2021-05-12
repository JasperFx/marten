using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace Marten.Events.Projections
{
    // Leave public for codegen!
    public interface ILiveAggregator<T>
    {
        T Build(IReadOnlyList<IEvent> events, IQuerySession session, T? snapshot);
        ValueTask<T> BuildAsync(
            IReadOnlyList<IEvent> events,
            IQuerySession session,
            T? snapshot,
            CancellationToken cancellation);

    }
}
