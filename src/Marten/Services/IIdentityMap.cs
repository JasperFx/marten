using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services
{
    public interface IIdentityMap
    {
        T Get<T>(object id, Func<string> json) where T : class;


        Task<T> GetAsync<T>(object id, Func<CancellationToken, Task<string>> json, CancellationToken token = default(CancellationToken)) where T : class;


        T Get<T>(object id, string json) where T : class;
        T Get<T>(object id, Type concreteType, string json) where T : class;

        

        void Remove<T>(object id);

        void Store<T>(object id, T entity);

        bool Has<T>(object id);

        T Retrieve<T>(object id) where T : class;
    }
}