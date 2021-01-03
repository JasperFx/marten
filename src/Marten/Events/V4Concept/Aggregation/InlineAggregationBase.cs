using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Operations;
using Marten.Storage;

namespace Marten.Events.V4Concept.Aggregation
{
    public abstract class InlineAggregationBase<TDoc, TId>: IInlineProjection
    {
        private readonly IEventSlicer<TDoc, TId> _slicer;
        private readonly ITenancy _tenancy;

        public InlineAggregationBase(IAggregateProjection projection, IEventSlicer<TDoc, TId> slicer, ITenancy tenancy)
        {
            _slicer = slicer;
            _tenancy = tenancy;
            ProjectionName = projection.ProjectionName;
            Projection = projection;

        }

        public IAggregateProjection Projection { get; }

        public string ProjectionName { get; }

        public void Apply(IDocumentSession session, IReadOnlyList<StreamAction> streams)
        {
            ApplyAsync(session, streams, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task ApplyAsync(IDocumentSession session, IReadOnlyList<StreamAction> streams,
            CancellationToken cancellation)
        {
            var slices = _slicer.Slice(streams, _tenancy);

            foreach (var slice in slices)
            {
                var operation = await DetermineOperation(session, slice, cancellation);
                session.QueueOperation(operation);
            }
        }

        public abstract Task<IStorageOperation> DetermineOperation(IDocumentSession session,
            EventSlice<TDoc, TId> slice, CancellationToken cancellation);
    }
}
