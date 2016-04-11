using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Linq
{
    public interface IMartenQueryProvider : IQueryProvider
    {
        Task<IList<TResult>> ExecuteCollectionAsync<TResult>(Expression expression, CancellationToken token);
        Task<IEnumerable<string>> ExecuteJsonCollectionAsync<TResult>(Expression expression, CancellationToken token);
        IEnumerable<string> ExecuteJsonCollection<TResult>(Expression expression);
        Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken token);
        Task<string> ExecuteJsonAsync<TResult>(Expression expression, CancellationToken token);
        string ExecuteJson<TResult>(Expression expression);
    }
}