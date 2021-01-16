using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Projections;
using Marten.Linq.SqlGeneration;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{
    public interface IAsyncProjectionShard
    {
        ISqlFragment[] EventFilters { get; }
        string ProjectionOrShardName { get; }
        AsyncOptions Options { get; }
        ITargetBlock<EventRange> Start(IProjectionUpdater updater, ILogger<IProjection> logger);

        Task Stop();
    }
}
