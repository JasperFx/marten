using System;
using System.Linq;
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

        public ISerializer Serializer => _serializer;

        public T Get<T>(object id, Func<FetchResult<T>> result) where T : class
        {
            return result()?.Document;
        }

        public async Task<T> GetAsync<T>(object id, Func<CancellationToken, Task<FetchResult<T>>> result, CancellationToken token = default(CancellationToken)) where T : class
        {
            var fetchResult = await result(token).ConfigureAwait(false);
            return fetchResult?.Document;
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

        public void Store<T>(object id, T entity) where T : class
        {
            // nothing
        }

        public bool Has<T>(object id) where T : class
        {
            return false;
        }

        public T Retrieve<T>(object id) where T : class
        {
            return null;
        }

        public IIdentityMap ForQuery()
        {
            return new IdentityMap(_serializer, Enumerable.Empty<IDocumentSessionListener>());
        }
    }
}