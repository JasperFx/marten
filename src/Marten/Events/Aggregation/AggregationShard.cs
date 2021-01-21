using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Linq.SqlGeneration;
using Marten.Storage;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Aggregation
{

    internal class AggregationShard<TDoc, TId> : AsyncProjectionShardBase<TenantSliceRange<TDoc, TId>>
    {
        private readonly AggregationRuntime<TDoc, TId> _runtime;
        private readonly ITenancy _tenancy;
        private TransformBlock<EventRange, TenantSliceRange<TDoc, TId>> _slicing;
        private ActionBlock<TenantSliceRange<TDoc, TId>> _building;
        private IProjectionUpdater _updater;
        private ILogger<IProjection> _logger;
        private CancellationToken _token;

        public AggregationShard(string projectionOrShardName, ISqlFragment[] eventFilters,
            AggregationRuntime<TDoc, TId> runtime, DocumentStore store, AsyncOptions options) : base(projectionOrShardName, eventFilters, store, options)
        {
            _runtime = runtime;
            _tenancy = store.Tenancy;
        }

        protected override Task configureUpdateBatch(ProjectionUpdateBatch batch, TenantSliceRange<TDoc, TId> sliceGroup, CancellationToken token)
        {
            return _runtime.Configure(batch.Queue, sliceGroup.Groups, _token);
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

        public EventRange Range { get; }
        public IReadOnlyList<TenantSliceGroup<TDoc, TId>> Groups { get; }

        public override string ToString()
        {
            return $"Aggregate for {Range}, {Groups.Count} slices";
        }

        public void Dispose()
        {
            foreach (var @group in Groups)
            {
                @group.Dispose();
            }
        }
    }
}
