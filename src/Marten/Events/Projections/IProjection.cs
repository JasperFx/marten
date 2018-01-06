using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async;
using Marten.Storage;

namespace Marten.Events.Projections
{
    public interface IProjection
    {
        Type[] Consumes { get; }
        
        AsyncOptions AsyncOptions { get; }
        void Apply(IDocumentSession session, EventPage page);
        Task ApplyAsync(IDocumentSession session, EventPage page, CancellationToken token);
        void EnsureStorageExists(ITenant tenant);
    }

    public static class ProjectionExtensions
    {
        public static Type ProjectedType(this IProjection projection)
        {
            return (projection as IDocumentProjection)?.Produces ?? projection.GetType();
        }

        public static Type[] ProjectedTypes(this IProjection projection)
        {
            switch (projection)
            {
                case IDocumentsProjection documentsProjection: return documentsProjection.Produces;
                default: return new[] {projection.ProjectedType()};
            }
        }
    }
}