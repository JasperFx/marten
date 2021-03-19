using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Linq.SqlGeneration;
using Marten.Storage;
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

        EventRangeGroup GroupEvents(IDocumentStore documentStore, ITenancy storeTenancy, EventRange range,
            CancellationToken cancellationToken);
    }
}
