using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;

namespace Marten.Events.V4Concept
{
    public interface IEventAppender
    {
        IEnumerable<IStorageOperation> BuildAppendOperations(
            IMartenSession session,
            IReadOnlyList<StreamAction> streams);

        Task<IEnumerable<IStorageOperation>> BuildAppendOperationsAsync(
            IMartenSession session,
            IReadOnlyList<StreamAction> streams,
            CancellationToken cancellation);

        void MarkTombstones(IReadOnlyList<StreamAction> streams);

    }
}
