using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Daemon;
using Marten.Internal.Operations;
using Marten.Linq.SqlGeneration;
using Marten.Storage;

namespace Marten.Events.Aggregation
{
    internal class AggregationShard<TDoc, TId>: AsyncProjectionShardBase<TenantSliceRange<TDoc, TId>>
    {
        private readonly AggregationRuntime<TDoc, TId> _runtime;
        private readonly ITenancy _tenancy;
        private DocumentStore _store;

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

        protected override TenantSliceRange<TDoc, TId> applyGrouping(EventRange range)
        {
            var groups = _runtime.Slicer.Slice(range.Events, _tenancy);
            return new TenantSliceRange<TDoc, TId>(_store, _runtime, range, groups, Cancellation);
        }
    }
}
