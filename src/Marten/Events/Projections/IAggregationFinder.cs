using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections
{
    public interface IAggregationFinder<T>
    {
        T Find(EventStream stream, IDocumentSession session);

        // TODO -- make this use the batch query later
        Task<T> FindAsync(EventStream stream, IDocumentSession session, CancellationToken token);
    }
}