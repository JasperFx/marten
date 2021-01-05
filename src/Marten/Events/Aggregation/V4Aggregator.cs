using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Internal.Operations;
using Marten.Linq.SqlGeneration;

namespace Marten.Events.Aggregation
{
    public abstract class V4Aggregator<TDoc, TId>
    {
        // This is what would be generated.
        public abstract ValueTask<IStorageOperation> ResolveOperation(EventSlice<TDoc, TId> fragment);
        public abstract ValueTask<TDoc> Create(IEvent @event);

        // This will determine the action too
        public abstract void SplitAndRoute(IReadOnlyList<IEvent> events, AggregatedPage<TDoc, TId> page);
        public Task<IAsyncBatch> Fetch(long floor, long ceiling, CancellationToken token)
        {
            // TODO -- *Think* this won't be generated. Just the SQL WHERE clause may vary a little bit
            throw new System.NotImplementedException();
        }

        public abstract Task<IReadOnlyList<TDoc>> LoadDocuments(IEnumerable<TId> ids, CancellationToken cancellation);

        public ISqlFragment EventFilter()
        {
            throw new System.NotImplementedException();
        }
    }
}
