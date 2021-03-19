using System.Threading;
using Marten.Events.Daemon;
using Marten.Linq.SqlGeneration;
using Marten.Storage;

namespace Marten.Events.Aggregation
{
    internal class AggregationShard<TDoc, TId>: AsyncProjectionShardBase
    {
        private readonly AggregationRuntime<TDoc, TId> _runtime;

        public AggregationShard(ShardName identifier, ISqlFragment[] eventFilters,
            AggregationRuntime<TDoc, TId> runtime, AsyncOptions options): base(identifier,
            eventFilters, options)
        {
            _runtime = runtime;
        }

        public override EventRangeGroup GroupEvents(IDocumentStore documentStore, ITenancy storeTenancy,
            EventRange range,
            CancellationToken cancellationToken)
        {
            var groups = _runtime.Slicer.Slice(range.Events, storeTenancy);
            return new TenantSliceRange<TDoc, TId>(documentStore, _runtime, range, groups, cancellationToken);
        }
    }
}
