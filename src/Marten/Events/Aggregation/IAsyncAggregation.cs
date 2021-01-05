using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Internal;
using Marten.Internal.Operations;

namespace Marten.Events.Aggregation
{
    public interface IAsyncAggregation<TDoc, TId>
    {

        public Task<IStorageOperation> DetermineOperation(IMartenSession session, EventSlice<TDoc, TId> slice, CancellationToken cancellation);

        public bool WillDelete(EventSlice<TDoc, TId> slice);
        IEventSlicer<TDoc, TId> Slicer { get; }
    }

    public abstract class AsyncDaemonAggregationBase<TDoc, TId> : IAsyncAggregation<TDoc, TId>
    {
        public IEventSlicer<TDoc, TId> Slicer { get; }
        private readonly IAggregateProjection _projection;

        public AsyncDaemonAggregationBase(IAggregateProjection projection, IEventSlicer<TDoc, TId> slicer)
        {
            Slicer = slicer;
            _projection = projection;
        }

        public bool WillDelete(EventSlice<TDoc, TId> slice)
        {
            return _projection.MatchesAnyDeleteType(slice);
        }

        public abstract Task<IStorageOperation> DetermineOperation(IMartenSession session, EventSlice<TDoc, TId> slice, CancellationToken cancellation);
    }

    public abstract class SyncDaemonAggregationBase<TDoc, TId> : IAsyncAggregation<TDoc, TId>
    {
        public IEventSlicer<TDoc, TId> Slicer { get; set; }
        private readonly IAggregateProjection _projection;

        public SyncDaemonAggregationBase(IAggregateProjection projection, IEventSlicer<TDoc, TId> slicer)
        {
            Slicer = slicer;
            _projection = projection;
        }

        public bool WillDelete(EventSlice<TDoc, TId> slice)
        {
            return _projection.MatchesAnyDeleteType(slice);
        }

        public Task<IStorageOperation> DetermineOperation(IMartenSession session, EventSlice<TDoc, TId> slice,
            CancellationToken cancellation)
        {
            return Task.FromResult(DetermineOperationSync(session, slice));
        }

        public abstract IStorageOperation DetermineOperationSync(IMartenSession session, EventSlice<TDoc,TId> slice);
    }
}
