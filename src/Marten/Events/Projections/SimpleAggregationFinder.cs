using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections
{
    public class SimpleAggregationFinder<T> : IAggregationFinder<T> where T : class, new()
    {
        public T Find(EventStream stream, IDocumentSession session)
        {
            return stream.IsNew ? new T() : session.Load<T>(stream.Id) ?? new T();
        }

        public async Task<T> FindAsync(EventStream stream, IDocumentSession session, CancellationToken token)
        {
            return stream.IsNew ? new T() : await session.LoadAsync<T>(stream.Id, token) ?? new T();
        }
    }
}