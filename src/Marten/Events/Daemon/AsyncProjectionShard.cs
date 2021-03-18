using System.Linq;
using Marten.Events.Projections;
using Marten.Linq.SqlGeneration;

namespace Marten.Events.Daemon
{
    internal class AsyncProjectionShard: AsyncProjectionShardBase
    {
        private readonly IProjection _projection;

        public AsyncProjectionShard(ShardName identifier, IProjection projection, ISqlFragment[] eventFilters, DocumentStore store,
            AsyncOptions options): base(identifier, eventFilters, store, options)
        {
            _projection = projection;
        }

        protected override EventRangeGroup applyGrouping(EventRange range)
        {
            return new TenantedEventRange(Store, _projection, range, Cancellation);
        }
    }
}
