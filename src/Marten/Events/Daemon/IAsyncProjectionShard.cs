using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Projections;
using Marten.Linq.SqlGeneration;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Identity for a single async shard
    /// </summary>
    public class ShardName
    {
        public const string All = "All";

        public ShardName(string projectionName, string key)
        {
            ProjectionName = projectionName;
            Key = key;
            Identity = $"{projectionName}:{key}";
        }

        public ShardName(string projectionName) : this(projectionName, All)
        {
        }

        public string ProjectionName { get; }
        public string Key { get; }

        public string Identity { get; }

        public override string ToString()
        {
            return $"{nameof(Identity)}: {Identity}";
        }



        protected bool Equals(ShardName other)
        {
            return Identity == other.Identity;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ShardName) obj);
        }

        public override int GetHashCode()
        {
            return (Identity != null ? Identity.GetHashCode() : 0);
        }
    }

    public interface IAsyncProjectionShard
    {
        ISqlFragment[] EventFilters { get; }
        ShardName Name { get; }
        AsyncOptions Options { get; }
        ITargetBlock<EventRange> Start(IProjectionUpdater updater, ILogger logger,
            CancellationToken token);

        Task Stop();
    }
}
