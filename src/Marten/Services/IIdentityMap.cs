using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services
{
    public interface IIdentityMap
    {
        T Get<T>(object id, Func<FetchResult<T>> result) where T : class;

        Task<T> GetAsync<T>(object id, Func<CancellationToken, Task<FetchResult<T>>> result, CancellationToken token = default(CancellationToken)) where T : class;


        T Get<T>(object id, string json) where T : class;
        T Get<T>(object id, Type concreteType, string json) where T : class;

        ISerializer Serializer { get; }

        void Remove<T>(object id);

        void Store<T>(object id, T entity) where T : class;

        bool Has<T>(object id) where T : class;

        T Retrieve<T>(object id) where T : class;

        IIdentityMap ForQuery();
    }
}