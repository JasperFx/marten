using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Linq
{
    public interface IMartenQueryable<T>
    {
        Task<IEnumerable<T>> ExecuteCollectionAsync(CancellationToken token);
    }
}