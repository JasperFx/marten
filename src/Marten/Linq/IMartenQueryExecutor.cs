using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Remotion.Linq;

namespace Marten.Linq
{
    public interface IMartenQueryExecutor : IQueryExecutor
    {
        Task<IEnumerable<T>> ExecuteCollectionAsync<T>(QueryModel queryModel, CancellationToken token);
        Task<T> ExecuteAsync<T>(QueryModel queryModel, CancellationToken token);
    }
}