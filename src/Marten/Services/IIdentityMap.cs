using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services
{
    public class FetchResult<T>
    {
        public FetchResult(T document, string json)
        {
            Document = document;
            Json = json;
        }

        public T Document { get; }

        public string Json { get; }
    }

    public interface IIdentityMap
    {
        T Get<T>(object id, Func<FetchResult<T>> result) where T : class;

        Task<T> GetAsync<T>(object id, Func<CancellationToken, Task<FetchResult<T>>> result, CancellationToken token = default(CancellationToken)) where T : class;


        T Get<T>(object id, string json) where T : class;
        T Get<T>(object id, Type concreteType, string json) where T : class;

        

        void Remove<T>(object id);

        void Store<T>(object id, T entity);

        bool Has<T>(object id);

        T Retrieve<T>(object id) where T : class;
    }
}