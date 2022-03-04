using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Storage;

namespace Marten.Events.Aggregation
{
    internal class TenantSliceRange<TDoc, TId>: EventRangeGroup
    {
        private readonly DocumentStore _store;
        private readonly IAggregationRuntime<TDoc, TId> _runtime;

        public TenantSliceRange(DocumentStore store, IAggregationRuntime<TDoc, TId> runtime, EventRange range,
            IReadOnlyList<TenantSliceGroup<TDoc, TId>> groups, CancellationToken projectionCancellation) : base(range, projectionCancellation)
        {
            _store = store;
            _runtime = runtime;
            Groups = groups;
        }

        public IReadOnlyList<TenantSliceGroup<TDoc, TId>> Groups { get; private set; }


        protected override void reset()
        {
            foreach (var group in Groups) group.Reset();
        }

        public override void Dispose()
        {
            foreach (var group in Groups) group.Dispose();
        }

        public override string ToString()
        {
            return $"Aggregate for {Range}, {Groups.Count} slices";
        }

        public override async Task ConfigureUpdateBatch(IShardAgent shardAgent, ProjectionUpdateBatch batch)
        {
#if NET6_0_OR_GREATER
            await Parallel.ForEachAsync(Groups, CancellationToken.None,
                    async (group, _) =>
                        await group.Start(shardAgent, batch, _runtime, _store, this).ConfigureAwait(false))
                .ConfigureAwait(false);
#else
            var eventGroupTasks = Groups
                .Select(x => x.Start(shardAgent, batch, _runtime, _store, this))
                .ToArray();

            await Task.WhenAll(eventGroupTasks).ConfigureAwait(false);
#endif


            if (Exception != null)
            {
                ExceptionDispatchInfo.Capture(Exception).Throw();
            }
        }

        public override async ValueTask SkipEventSequence(long eventSequence, IMartenDatabase database)
        {
            reset();
            Range.SkipEventSequence(eventSequence);

            Groups = await _runtime.GroupEventRange(_store, database, Range, Cancellation).ConfigureAwait(false);
        }
    }
}
