using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Linq.SqlGeneration;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Projection update builder for a single projection shard running asynchronously.
    /// This service is passive.
    /// </summary>
    public interface IAsyncProjectionShard
    {
        /// <summary>
        /// WHERE clause fragments used to filter the events
        /// to be applied to this projection shard
        /// </summary>
        ISqlFragment[] EventFilters { get; }

        /// <summary>
        /// The identity of this projection shard
        /// </summary>
        ShardName Name { get; }

        /// <summary>
        /// Daemon configuration for this specific projection shard
        /// </summary>
        AsyncOptions Options { get; }

        /// <summary>
        /// Incorporate this projection shard into a running projection daemon
        /// </summary>
        /// <param name="updater"></param>
        /// <param name="logger"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        ITargetBlock<EventRange> Start(IProjectionUpdater updater, ILogger logger,
            CancellationToken token);

        /// <summary>
        /// Stop any work happening inside the projection shard
        /// </summary>
        /// <returns></returns>
        Task Stop();
    }
}
