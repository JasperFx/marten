using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Linq.SqlGeneration;
using Marten.Storage;

namespace Marten.Events.Aggregation
{
    public class AggregationShard<TDoc, TId> : IAsyncProjectionShard
    {
        private readonly AggregationRuntime<TDoc, TId> _runtime;
        private readonly ITenancy _tenancy;
        private TransformBlock<EventRange, AggregateRange> _slicing;
        private ActionBlock<AggregateRange> _building;
        private IProjectionUpdater _updater;

        public AggregationShard(string projectionOrShardName, ISqlFragment[] eventFilters,
            AggregationRuntime<TDoc, TId> runtime, ITenancy tenancy)
        {
            _runtime = runtime;
            _tenancy = tenancy;
            EventFilters = eventFilters;
            ProjectionOrShardName = projectionOrShardName;
        }

        public ISqlFragment[] EventFilters { get; }

        public string ProjectionOrShardName { get; }

        public ITargetBlock<EventRange> Start(IProjectionUpdater updater)
        {
            _updater = updater;

            var singleFileOptions = new ExecutionDataflowBlockOptions
            {
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            };

            _slicing = new TransformBlock<EventRange, AggregateRange>(x => slice(x), singleFileOptions);


            _building = new ActionBlock<AggregateRange>(configureBatch, singleFileOptions);

            _slicing.LinkTo(_building);

            return _slicing;
        }

        private async Task configureBatch(AggregateRange aggregateRange)
        {
            var batch = _updater.StartNewBatch(aggregateRange.Range);
            await _runtime.Configure(batch.Queue, aggregateRange.Slices);
            await _updater.ExecuteBatch(batch);
        }

        internal class AggregateRange
        {
            public AggregateRange(EventRange range, IReadOnlyList<EventSlice<TDoc, TId>> slices)
            {
                Range = range;
                Slices = slices;
            }

            public EventRange Range { get; }
            public IReadOnlyList<EventSlice<TDoc, TId>> Slices { get; }

        }

        private AggregateRange slice(EventRange range)
        {
            var slices = _runtime.Slicer.Slice(range.Events, _tenancy);
            return new AggregateRange(range, slices);
        }

        public async Task Stop()
        {
            await _slicing.Completion;
            await _building.Completion;

            _slicing.Complete();
            _building.Complete();
        }
    }
}
