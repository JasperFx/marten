using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services
{
    public interface IIdentityMap
    {
        // Add the version to FetchResult here, see that it gets in the version
        T Get<T>(object id, Func<FetchResult<T>> result);

        // Add the version to FetchResult here, see that it gets in the version
        Task<T> GetAsync<T>(object id, Func<CancellationToken, Task<FetchResult<T>>> result, CancellationToken token = default(CancellationToken));


        T Get<T>(object id, string json, Guid? version);
        T Get<T>(object id, Type concreteType, string json, Guid? version);

        ISerializer Serializer { get; }

        void Remove<T>(object id);

        void Store<T>(object id, T entity, Guid? version = null);

        bool Has<T>(object id);

        T Retrieve<T>(object id);

        IIdentityMap ForQuery();

        VersionTracker Versions { get; }
    }
}