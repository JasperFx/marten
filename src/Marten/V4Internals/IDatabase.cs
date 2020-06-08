using System.Threading;
using System.Threading.Tasks;

namespace Marten.V4Internals
{
    public interface IDatabase
    {
        T Execute<T>(IQueryHandler<T> handler, IMartenSession session);
        Task<T> ExecuteAsync<T>(IQueryHandler<T> handler, IMartenSession session, CancellationToken token);

        int RequestCount { get; }
    }
}