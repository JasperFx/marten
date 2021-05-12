using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Base class for projections that are strictly synchronous
    /// </summary>
    public abstract class SyncProjectionBase: IProjection
    {
        public abstract void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams);

        public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
            CancellationToken cancellation)
        {
            Apply(operations, streams);
            return Task.CompletedTask;
        }
    }
}
