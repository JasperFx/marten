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
        IEnumerable<string> ExecuteCollectionToJson<T>(QueryModel queryModel);
        Task<IEnumerable<string>> ExecuteCollectionToJsonAsync<T>(QueryModel queryModel, CancellationToken token);
        string ExecuteSingleToJson<T>(QueryModel queryModel, bool returnDefaultWhenEmpty);
        Task<string> ExecuteSingleToJsonAsync<T>(QueryModel queryModel, bool returnDefaultWhenEmpty, CancellationToken token);
        string ExecuteFirstToJson<T>(QueryModel queryModel, bool returnDefaultWhenEmpty);
        Task<string> ExecuteFirstToJsonAsync<T>(QueryModel queryModel, bool returnDefaultWhenEmpty, CancellationToken token);
    }
}