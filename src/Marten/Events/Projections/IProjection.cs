using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Storage;

namespace Marten.Events.Projections
{
    public interface IProjection
    {
        void Apply(IDocumentSession session, IReadOnlyList<StreamAction> streams);

        Task ApplyAsync(IDocumentSession session, IReadOnlyList<StreamAction> streams, CancellationToken cancellation);


    }


    public abstract class SynchronousProjectionBase: IProjection
    {
        public abstract void Apply(IDocumentSession session, IReadOnlyList<StreamAction> streams);

        public Task ApplyAsync(IDocumentSession session, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
        {
            Apply(session, streams);
            return Task.CompletedTask;
        }
    }

    public abstract class AsynchronousProjectionBase: IProjection
    {
        public void Apply(IDocumentSession session, IReadOnlyList<StreamAction> streams)
        {
            ApplyAsync(session, streams, CancellationToken.None).GetAwaiter().GetResult();
        }

        public abstract Task ApplyAsync(IDocumentSession session, IReadOnlyList<StreamAction> streams,
            CancellationToken cancellation);
    }
}
