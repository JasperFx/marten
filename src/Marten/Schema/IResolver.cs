using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Schema
{
    public interface IResolver<T>
    {
        T Resolve(DbDataReader reader, IIdentityMap map);
        Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token);
        T Build(DbDataReader reader, ISerializer serializer);

        T Resolve(IIdentityMap map, ILoader loader, object id);
        Task<T> ResolveAsync(IIdentityMap map, ILoader loader, CancellationToken token, object id);
    }
}