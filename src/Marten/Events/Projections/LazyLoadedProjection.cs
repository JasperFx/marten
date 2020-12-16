using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async;
using Marten.Storage;
using Marten.Util;

namespace Marten.Events.Projections
{
    [Obsolete("This will be eliminated in V4 and replaced w/ the new ViewProjection")]
    public class LazyLoadedProjection<T>: IProjection, IDocumentsProjection
        where T : IProjection
    {
        private readonly Func<T> factory;

        public LazyLoadedProjection(Func<T> factory)
        {
            this.factory = factory;
            var definition = New<T>.Instance();

            Consumes = definition.Consumes;
            AsyncOptions = definition.AsyncOptions;
            Produces = (definition as IDocumentsProjection)?.Produces;
            if (Produces?.Any() != true && definition is IDocumentProjection documentProjection)
                Produces = new[] { documentProjection.Produces };
        }

        public Type[] Consumes { get; }

        public AsyncOptions AsyncOptions { get; }

        public void Apply(IDocumentSession session, EventPage page)
        {
            factory().Apply(session, page);
        }

        public Task ApplyAsync(IDocumentSession session, EventPage page, CancellationToken token)
        {
            return factory().ApplyAsync(session, page, token);
        }

        public void EnsureStorageExists(ITenant tenant)
        {
            factory().EnsureStorageExists(tenant);
        }

        public Type[] Produces { get; }
    }
}
