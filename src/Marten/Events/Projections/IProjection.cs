using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async;
using Marten.Events.V4Concept;
using Marten.Internal.Sessions;
using Marten.Storage;

namespace Marten.Events.Projections
{
    [Obsolete("This will be eliminated in V4 and replaced w/ the new ViewProjection")]
    public interface IProjection
    {
        Type[] Consumes { get; }

        AsyncOptions AsyncOptions { get; }

        void Apply(IDocumentSession session, EventPage page);

        Task ApplyAsync(IDocumentSession session, EventPage page, CancellationToken token);
        void EnsureStorageExists(ITenant tenant);
    }

    [Obsolete("Remove later")]
    public class TemporaryV4InlineShim: IInlineProjection
    {
        private readonly IProjection _inner;

        public TemporaryV4InlineShim(IProjection inner)
        {
            _inner = inner;
        }

        public string ProjectionName => _inner.GetEventProgressionName();
        public void Apply(IDocumentSession session, IReadOnlyList<StreamAction> streams)
        {
            _inner.Apply(session, new EventPage(streams.Select(x => x.ShimForOldProjections()).ToArray()));
        }

        public Task ApplyAsync(IDocumentSession session, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
        {
            return _inner.ApplyAsync(session, new EventPage(streams.Select(x => x.ShimForOldProjections()).ToArray()), cancellation);
        }
    }

    public static class ProjectionExtensions
    {
        public static Type ProjectedType(this IProjection projection)
        {
            return (projection as IDocumentProjection)?.Produces ?? projection.GetType();
        }

        public static string GetEventProgressionName(this IProjection projection)
        {
            return (projection as IHasCustomEventProgressionName)?.Name ?? projection.ProjectedType().FullName;
        }

        public static string GetEventProgressionName(this IProjection projection, Type type)
        {
            return (projection as IHasCustomEventProgressionName)?.Name ?? type.FullName;
        }

        public static Type[] ProjectedTypes(this IProjection projection)
        {
            switch (projection)
            {
                default:
                    return new[] { projection.ProjectedType() };
            }
        }
    }
}
