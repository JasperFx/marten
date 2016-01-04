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
        NpgsqlCommand BuildCommand(QueryModel queryModel);
        NpgsqlCommand BuildCommand<T>(IQueryable<T> queryable);
        Task<IEnumerable<T>> ExecuteCollectionAsync<T>(QueryModel queryModel, CancellationToken token);
    }
}