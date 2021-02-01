using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Linq.SqlGeneration;
using Marten.Storage;

namespace Marten.Events.Aggregation
{
    internal class AggregationShard<TDoc, TId>: AsyncProjectionShardBase<TenantSliceRange<TDoc, TId>>
    {
        private readonly AggregationRuntime<TDoc, TId> _runtime;
        private readonly ITenancy _tenancy;

        public AggregationShard(ShardName identifier, ISqlFragment[] eventFilters,
            AggregationRuntime<TDoc, TId> runtime, DocumentStore store, AsyncOptions options): base(identifier,
            eventFilters, store, options)
        {
            _runtime = runtime;
            _tenancy = store.Tenancy;
        }

        protected override void ensureStorageExists()
        {
            Store.Tenancy.Default.EnsureStorageExists(typeof(TDoc));
        }

        protected override Task configureUpdateBatch(ProjectionUpdateBatch batch,
            TenantSliceRange<TDoc, TId> sliceGroup, CancellationToken token)
        {
            return _runtime.Configure(batch.Queue, sliceGroup.Groups, token);
        }

        protected override TenantSliceRange<TDoc, TId> applyGrouping(EventRange range)
        {
            var groups = _runtime.Slicer.Slice(range.Events, _tenancy);
            return new TenantSliceRange<TDoc, TId>(range, groups);
        }
    }

    internal class TenantSliceRange<TDoc, TId>: IEventRangeGroup
    {
        public TenantSliceRange(EventRange range, IReadOnlyList<TenantSliceGroup<TDoc, TId>> groups)
        {
            Range = range;
            Groups = groups;
        }

        public IReadOnlyList<TenantSliceGroup<TDoc, TId>> Groups { get; }

        public EventRange Range { get; }

        public void Reset()
        {
            foreach (var group in Groups) group.Reset();
        }

        public void Dispose()
        {
            foreach (var group in Groups) group.Dispose();
        }

        public override string ToString()
        {
            return $"Aggregate for {Range}, {Groups.Count} slices";
        }
    }
}
