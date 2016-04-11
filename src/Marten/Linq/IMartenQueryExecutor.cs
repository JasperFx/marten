using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Remotion.Linq;

namespace Marten.Linq
{
    public interface IMartenQueryExecutor : IQueryExecutor
    {
        Task<IList<T>> ExecuteCollectionAsync<T>(QueryModel queryModel, CancellationToken token);
        Task<T> ExecuteAsync<T>(QueryModel queryModel, CancellationToken token);
        Task<IEnumerable<string>> ExecuteCollectionToJsonAsync<T>(QueryModel queryModel, CancellationToken token);
        IEnumerable<string> ExecuteCollectionToJson<T>(QueryModel queryModel);
        Task<string> ExecuteJsonAsync<T>(QueryModel queryModel, CancellationToken token);
        string ExecuteJson<T>(QueryModel queryModel);
    }
}