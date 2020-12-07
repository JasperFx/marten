using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections
{
    public interface IAggregationFinder<T>
    {
        T Find(StreamAction stream, IDocumentSession session);

        Task<T> FindAsync(StreamAction stream, IDocumentSession session, CancellationToken token);

        Task FetchAllAggregates(IDocumentSession session, StreamAction[] streams, CancellationToken token);
    }
}
