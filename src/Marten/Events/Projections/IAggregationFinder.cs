using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections
{
    public interface IAggregationFinder<T>
    {
        T Find(EventStream stream, IDocumentSession session);

        Task<T> FindAsync(EventStream stream, IDocumentSession session, CancellationToken token);

        Task FetchAllAggregates(IDocumentSession session, EventStream[] streams, CancellationToken token);
    }
}
