using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;

namespace Marten.Schema
{
    public interface IResolver<T>
    {
        T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map);
        Task<T> ResolveAsync(int startingIndex, DbDataReader reader, IIdentityMap map, CancellationToken token);

        T Build(DbDataReader reader, ISerializer serializer);

        Task<T> BuildAsync(DbDataReader reader, ISerializer serializer, CancellationToken token);

        T Resolve(IIdentityMap map, ILoader loader, object id);
        Task<T> ResolveAsync(IIdentityMap map, ILoader loader, CancellationToken token, object id);

    }
}