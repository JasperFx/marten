using System.Linq;
using Npgsql;
using Remotion.Linq;

namespace Marten.Linq
{
    public interface IMartenQueryExecutor : IQueryExecutor
    {
        NpgsqlCommand BuildCommand(QueryModel queryModel);
        NpgsqlCommand BuildCommand<T>(IQueryable<T> queryable);
    }
}