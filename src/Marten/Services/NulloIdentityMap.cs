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

        public T Get<T>(object id, Func<FetchResult<T>> result)
        {
            var fetched = result();

            storeFetched(id, fetched);

            return fetched == null ? default(T) : fetched.Document;
        }

        private void storeFetched<T>(object id, FetchResult<T> fetched)
        {
            if (fetched?.Version != null)
            {
                Versions.Store<T>(id, fetched.Version.Value);
            }
        }

        public async Task<T> GetAsync<T>(object id, Func<CancellationToken, Task<FetchResult<T>>> result, CancellationToken token = default(CancellationToken))
        {
            var fetchResult = await result(token).ConfigureAwait(false);

            storeFetched(id, fetchResult);

            return fetchResult == null ? default(T) : fetchResult.Document;
        }

        public T Get<T>(object id, string json, Guid? version)
        {
            if (json.IsEmpty()) return default(T);

            if (version.HasValue)
            {
                Versions.Store<T>(id, version.Value);
            }

            return _serializer.FromJson<T>(json);
        }

        public T Get<T>(object id, Type concreteType, string json, Guid? version)
        {
            if (json.IsEmpty()) return default(T);

            if (version.HasValue)
            {
                Versions.Store<T>(id, version.Value);
            }

            return _serializer.FromJson(concreteType, json).As<T>();
        }

        public void Remove<T>(object id)
        {
            // nothing
        }

        public void Store<T>(object id, T entity, Guid? version = null)
        {
            if (version.HasValue)
            {
                Versions.Store<T>(id, version.Value);
            }
        }

        public bool Has<T>(object id)
        {
            return false;
        }

        public T Retrieve<T>(object id)
        {
            return default(T);
        }

        public IIdentityMap ForQuery()
        {
            return new IdentityMap(_serializer, Enumerable.Empty<IDocumentSessionListener>())
            {
                Versions = Versions
            };
        }

        public VersionTracker Versions { get; } = new VersionTracker();
    }
}