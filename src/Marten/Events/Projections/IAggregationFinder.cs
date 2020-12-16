using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections
{
    [Obsolete("This will be eliminated in V4")]
    public interface IAggregationFinder<T>
    {
        T Find(StreamAction stream, IDocumentSession session);

        Task<T> FindAsync(StreamAction stream, IDocumentSession session, CancellationToken token);

        Task FetchAllAggregates(IDocumentSession session, StreamAction[] streams, CancellationToken token);
    }
}
