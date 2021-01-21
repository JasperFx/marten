using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections
{
    public abstract class AsyncEventProjectionBase: AsyncProjectionBase
    {
        public override async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
            CancellationToken cancellation)
        {
            foreach (var stream in streams)
            {
                foreach (var @event in stream.Events)
                {
                    await ApplyEvent(operations, stream, @event, cancellation);
                }
            }
        }

        public abstract Task ApplyEvent(IDocumentOperations operations, StreamAction streamAction, IEvent e,
            CancellationToken cancellationToken);

    }
}