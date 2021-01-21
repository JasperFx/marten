using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Storage;

namespace Marten.Events.Projections
{
    public interface IProjection
    {
        void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams);

        Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
            CancellationToken cancellation);
    }

}
