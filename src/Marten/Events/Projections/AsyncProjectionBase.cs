using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections
{
    public abstract class AsyncProjectionBase: IProjection
    {
        public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
        {
            ApplyAsync(operations, streams, CancellationToken.None).GetAwaiter().GetResult();
        }

        public abstract Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
            CancellationToken cancellation);
    }
}