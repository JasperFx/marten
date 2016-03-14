using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public interface ISelector<T>
    {
        T Resolve(DbDataReader reader, IIdentityMap map);

        Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token);

        string SelectClause(IDocumentMapping mapping);
    }
}