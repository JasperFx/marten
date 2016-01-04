using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Linq
{
    public interface IMartenQueryProvider : IQueryProvider
    {
        Task<IEnumerable<TResult>> ExecuteCollectionAsync<TResult>(Expression expression, CancellationToken token);
    }
}