using Marten.Events.Daemon;
using Marten.Linq.SqlGeneration;
using Marten.Storage;

namespace Marten.Events.Aggregation
{
    internal class AggregationShard<TDoc, TId>: AsyncProjectionShardBase
    {
        private readonly AggregationRuntime<TDoc, TId> _runtime;
        private readonly ITenancy _tenancy;
        private readonly DocumentStore _store;

        public AggregationShard(ShardName identifier, ISqlFragment[] eventFilters,
            AggregationRuntime<TDoc, TId> runtime, DocumentStore store, AsyncOptions options): base(identifier,
            eventFilters, store, options)
        {
            _runtime = runtime;
            _tenancy = store.Tenancy;
            _store = store;
        }

        protected override void ensureStorageExists()
        {
            Store.Tenancy.Default.EnsureStorageExists(typeof(TDoc));
        }

        protected override EventRangeGroup applyGrouping(EventRange range)
        {
            var groups = _runtime.Slicer.Slice(range.Events, _tenancy);
            return new TenantSliceRange<TDoc, TId>(_store, _runtime, range, groups, Cancellation);
        }
    }
}
