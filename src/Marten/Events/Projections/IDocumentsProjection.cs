using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async;
using Marten.Storage;

namespace Marten.Events.Projections
{
    [Obsolete("This will be eliminated in V4 and replaced w/ the new ViewProjection")]
    public interface IDocumentsProjection: IProjection
    {
        Type[] Produces { get; }
    }

    [Obsolete("This will be eliminated in V4 and replaced w/ the new ViewProjection")]
    public abstract class DocumentsProjection: IDocumentsProjection
    {
        public abstract Type[] Consumes { get; }
        public abstract Type[] Produces { get; }

        public abstract AsyncOptions AsyncOptions { get; }

        public abstract void Apply(IDocumentSession session, EventPage page);

        public abstract Task ApplyAsync(IDocumentSession session, EventPage page, CancellationToken token);

        public void EnsureStorageExists(ITenant tenant)
        {
            foreach (var type in Produces)
            {
                tenant.EnsureStorageExists(type);
            }
        }
    }
}
