#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections;

// Leave public for codegen!
public interface ILiveAggregator<T>
{
    ValueTask<T> BuildAsync(
        IReadOnlyList<IEvent> events,
        IQuerySession session,
        T? snapshot,
        CancellationToken cancellation);
}
