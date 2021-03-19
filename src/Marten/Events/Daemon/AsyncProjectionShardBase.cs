using System;
using System.Threading;
using Marten.Linq.SqlGeneration;
using Marten.Storage;

namespace Marten.Events.Daemon
{
    [Obsolete("This is going away.")]
    internal abstract class AsyncProjectionShardBase: IAsyncProjectionShard
    {
        protected AsyncProjectionShardBase(ShardName identifier, ISqlFragment[] eventFilters, AsyncOptions options)
        {
            Name = identifier;
            EventFilters = eventFilters;
            Options = options;
        }

        public ISqlFragment[] EventFilters { get; }
        public ShardName Name { get; }
        public AsyncOptions Options { get; }

        public abstract EventRangeGroup GroupEvents(IDocumentStore documentStore, ITenancy storeTenancy,
            EventRange range,
            CancellationToken cancellationToken);
    }
}
