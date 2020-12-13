using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Storage;
using Microsoft.CodeAnalysis;

namespace Marten.Events.V4Concept.Aggregation
{
    public interface IAsyncAggregation<TDoc, TId>
    {
        // TODO -- generate this a lot like the equivalent in the inline operation


        IReadOnlyList<StreamFragment<TDoc, TId>> Split(IEnumerable<IEvent> events, ITenancy storeTenancy);

        public Task<IStorageOperation> DetermineOperation(IMartenSession session, StreamFragment<TDoc, TId> fragment, CancellationToken cancellation);

        public bool WillDelete(StreamFragment<TDoc, TId> fragment);
    }

    public abstract class AsyncDaemonAggregationBase<TDoc, TId> : IAsyncAggregation<TDoc, TId>
    {
        private readonly IAggregateProjection _projection;

        public AsyncDaemonAggregationBase(IAggregateProjection projection)
        {
            _projection = projection;
        }

        public bool WillDelete(StreamFragment<TDoc, TId> fragment)
        {
            return _projection.MatchesAnyDeleteType(fragment);
        }

        public abstract IReadOnlyList<StreamFragment<TDoc, TId>> Split(IEnumerable<IEvent> events,
            ITenancy storeTenancy);
        public abstract Task<IStorageOperation> DetermineOperation(IMartenSession session, StreamFragment<TDoc, TId> fragment, CancellationToken cancellation);
    }

    public abstract class SyncDaemonAggregationBase<TDoc, TId> : IAsyncAggregation<TDoc, TId>
    {
        private readonly IAggregateProjection _projection;

        public SyncDaemonAggregationBase(IAggregateProjection projection)
        {
            _projection = projection;
        }

        public bool WillDelete(StreamFragment<TDoc, TId> fragment)
        {
            return _projection.MatchesAnyDeleteType(fragment);
        }

        public abstract IReadOnlyList<StreamFragment<TDoc, TId>> Split(IEnumerable<IEvent> events,
            ITenancy storeTenancy);

        public Task<IStorageOperation> DetermineOperation(IMartenSession session, StreamFragment<TDoc, TId> fragment,
            CancellationToken cancellation)
        {
            return Task.FromResult(DetermineOperationSync(session, fragment));
        }

        public abstract IStorageOperation DetermineOperationSync(IMartenSession session, StreamFragment<TDoc,TId> fragment);
    }
}
