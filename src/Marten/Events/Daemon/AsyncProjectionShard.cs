using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Projections;
using Marten.Linq.SqlGeneration;
using Marten.Storage;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Definition of a single projection shard to be executed asynchronously
    /// </summary>
    public class AsyncProjectionShard
    {
        public AsyncProjectionShard(string shardName, ProjectionSource source, ISqlFragment[] filters)
        {
            Name = new ShardName(source.ProjectionName, shardName);
            EventFilters = filters;
            Source = source;
        }

        public AsyncProjectionShard(ProjectionSource source, ISqlFragment[] filters)
        {
            Name = new ShardName(source.ProjectionName);
            EventFilters = filters;
            Source = source;
        }

        public ProjectionSource Source { get;}

        /// <summary>
        /// WHERE clause fragments used to filter the events
        /// to be applied to this projection shard
        /// </summary>
        public ISqlFragment[] EventFilters { get; }

        /// <summary>
        /// The identity of this projection shard
        /// </summary>
        public ShardName Name { get; }

    }
}
