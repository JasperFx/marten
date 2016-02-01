using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Linq
{
    public interface IMartenQueryable<T> : IQueryable<T>
    {
        Task<IEnumerable<T>> ExecuteCollectionAsync(CancellationToken token);
    }
}