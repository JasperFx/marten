using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Internal.Operations;
using Marten.Linq.SqlGeneration;

namespace Marten.Events.Daemon
{
    public interface IAsyncProjection
    {
        ISqlFragment[] EventFilters { get; }
        string ProjectionOrShardName { get; }
        Task Configure(ActionBlock<IStorageOperation> queue, IAsyncEnumerable<IEvent> events);
    }
}