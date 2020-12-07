using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;

namespace Marten.Events.V4Concept
{
    /// <summary>
    /// This will be used from within DocumentSessionBase
    /// </summary>
    internal class EventAppender: IEventAppender
    {
        private readonly EventGraph _graph;
        private readonly EventDocumentStorage _builder;
        private readonly IInlineProjection[] _projections;

        public EventAppender(EventGraph graph, EventDocumentStorage builder, IInlineProjection[] projections)
        {
            _graph = graph;
            _builder = builder;
            _projections = projections;

            // TODO -- track the event streams separately within the DocumentSessionBase
        }



        public IEnumerable<IStorageOperation> BuildAppendOperations(IMartenSession session, IReadOnlyList<StreamAction> streams)
        {
            //s.Events.AppendExclusive("some id", events); // This would lock the stream editing
            //s.Events.Append("some id", events); // Marten looks up the current stream version right here


            /*
             * GOALS
             * - Enable tombstone workflow by reserving
             * - Enable inline projections to use event metadata like Version
             * - Enforce that new streams from StartStream() do not already exist
             * - Better stream optimistic concurrency, could fetch at AppendEvent() time
             * - Set us up for the event metadata
             */

            // 1. load the stream data for each stream & reserve new sequence values.
            // 2. Assign the versions and sequence values to each individual event event
            // 3. emit an operation to update each stream version or insert a new stream
            // 4. emit an operation to insert each event
            // 5. emit operations for each inline projection
            throw new System.NotImplementedException();
        }

        public Task<IEnumerable<IStorageOperation>> BuildAppendOperationsAsync(IMartenSession session, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
        {
            throw new System.NotImplementedException();
        }

        public void MarkTombstones(IReadOnlyList<StreamAction> streams)
        {
            throw new System.NotImplementedException();
        }
    }
}
