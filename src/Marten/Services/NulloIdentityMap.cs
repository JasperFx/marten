using System;
using System.Threading;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Services
{
    public class NulloIdentityMap : IIdentityMap
    {
        private readonly ISerializer _serializer;

        public NulloIdentityMap(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public T Get<T>(object id, Func<string> json) where T : class
        {
            var text = json();
            return Get<T>(id, text);
        }

        public async Task<T> GetAsync<T>(object id, Func<CancellationToken, Task<string>> json, CancellationToken token) where T : class
        {
            var text = await json(token).ConfigureAwait(false);
            return Get<T>(id, text);
        }

        public T Get<T>(object id, string json) where T : class
        {
            if (json.IsEmpty()) return null;

            return _serializer.FromJson<T>(json);
        }

        public T Get<T>(object id, Type concreteType, string json) where T : class
        {
            if (json.IsEmpty()) return null;

            return _serializer.FromJson(concreteType, json).As<T>();
        }

        public void Remove<T>(object id)
        {
            // nothing
        }

        public void Store<T>(object id, T entity)
        {
            // nothing
        }

        public bool Has<T>(object id)
        {
            return false;
        }

        public T Retrieve<T>(object id) where T : class
        {
            return null;
        }
    }
}