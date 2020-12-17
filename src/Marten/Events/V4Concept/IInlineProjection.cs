using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.V4Concept
{
    public interface IInlineProjection
    {
        void Apply(IDocumentSession session, IReadOnlyList<StreamAction> streams);

        Task ApplyAsync(IDocumentSession session, IReadOnlyList<StreamAction> streams, CancellationToken cancellation);
    }
}
