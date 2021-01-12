using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Internal.Operations;
using Marten.Linq.SqlGeneration;

namespace Marten.Events.Daemon
{
    public interface IAsyncProjectionShard
    {
        ISqlFragment[] EventFilters { get; }
        string ProjectionOrShardName { get; }
        AsyncOptions Options { get; }
        ITargetBlock<EventRange> Start(IProjectionUpdater updater);

        Task Stop();
    }
}
