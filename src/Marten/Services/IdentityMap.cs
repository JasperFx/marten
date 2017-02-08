using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Services
{
    public enum UnitOfWorkOrigin
    {
        Stored,
        Loaded
    }

    public abstract class IdentityMap<TCacheValue> : IIdentityMap
    {
        private readonly IEnumerable<IDocumentSessionListener> _listeners;

        protected ConcurrentCache<Type, ConcurrentDictionary<object, TCacheValue>> Cache { get; }
            = new ConcurrentCache<Type, ConcurrentDictionary<object, TCacheValue>>(_ => new ConcurrentDictionary<object, TCacheValue>());

        public ISerializer Serializer { get; }

        protected IdentityMap(ISerializer serializer, IEnumerable<IDocumentSessionListener> listeners)
        {
            Serializer = serializer;
            _listeners = listeners ?? new IDocumentSessionListener[] { };
        }

        protected abstract TCacheValue ToCache(object id, Type concreteType, object document, string json, UnitOfWorkOrigin origin = UnitOfWorkOrigin.Loaded);
        protected abstract T FromCache<T>(TCacheValue cacheValue);

        private void storeFetched<T>(object id, FetchResult<T> fetched)
        {
            if (fetched?.Version != null)
            {
                Versions.Store<T>(id, fetched.Version.Value);
            }
        }

        public T Get<T>(object id, Func<FetchResult<T>> result)
        {
            var cacheValue = Cache[typeof(T)].GetOrAdd(id, _ =>
            {
                var fetchResult = result();

                storeFetched(id, fetchResult);

                var document = fetchResult == null ? default(T) : fetchResult.Document;
                
                _listeners.Each(listener => listener.DocumentLoaded(id, document));
                return ToCache(id, typeof(T), document, fetchResult?.Json);
            });


            return FromCache<T>(cacheValue);
        }

        public async Task<T> GetAsync<T>(object id, Func<CancellationToken, Task<FetchResult<T>>> result, CancellationToken token = default(CancellationToken))
        {
            var dictionary = Cache[typeof(T)];

            if (dictionary.ContainsKey(id))
            {
                return FromCache<T>(dictionary[id]);
            }

            var fetchResult = await result(token).ConfigureAwait(false);
            if (fetchResult == null) return default(T);

            storeFetched(id, fetchResult);

            var document = fetchResult.Document;

            dictionary[id] = ToCache(id, typeof(T), document, fetchResult.Json);

            _listeners.Each(listener => listener.DocumentLoaded(id, document));

            return document;
        }

        public T Get<T>(object id, string json, Guid? version)
        {
            return Get<T>(id, typeof(T), json, version);
        }

        public T Get<T>(object id, Type concreteType, string json, Guid? version)
        {
            var cacheValue = Cache[typeof(T)].GetOrAdd(id, _ =>
            {
                if (json.IsEmpty()) return ToCache(id, concreteType, null, json);

                if (version.HasValue)
                {
                    Versions.Store<T>(id, version.Value);
                }

                var document = Serializer.FromJson(concreteType, json);

                _listeners.Each(listener => listener.DocumentLoaded(id, document));

                return ToCache(id, concreteType, document, json);
            });
            return FromCache<T>(cacheValue);
        }

        public void Remove<T>(object id)
        {
            TCacheValue value;
            Cache[typeof(T)].TryRemove(id, out value);
        }

        public void Store<T>(object id, T entity, Guid? version = null)
        {
            if (version.HasValue)
            {
                Versions.Store<T>(id, version.Value);
            }

            var dictionary = Cache[typeof(T)];

            if (dictionary.ContainsKey(id) && dictionary[id] != null)
            {
                var existing = FromCache<T>(dictionary[id]);
                if (existing != null && !ReferenceEquals(existing, entity))
                {
                    throw new InvalidOperationException(
                        $"Document '{typeof(T).FullName}' with same Id already added to the session.");
                }
            }

            _listeners.Each(listener => listener.DocumentAddedForStorage(id, entity));

            var cacheValue = ToCache(id, typeof(T), entity, null, UnitOfWorkOrigin.Stored);

            dictionary.AddOrUpdate(id, cacheValue, (i, e) => cacheValue);
        }

        public bool Has<T>(object id) 
        {
            var dict = Cache[typeof(T)];
            return dict.ContainsKey(id) && FromCache<T>(dict[id]) != null;
        }

        public T Retrieve<T>(object id)
        {
            var dict = Cache[typeof(T)];
            return dict.ContainsKey(id) ? FromCache<T>(dict[id]) : default(T);
        }

        public IIdentityMap ForQuery()
        {
            return this;
        }

        public VersionTracker Versions { get; set; } = new VersionTracker();
    }

    public class IdentityMap : IdentityMap<object>
    {
        public IdentityMap(ISerializer serializer, IEnumerable<IDocumentSessionListener> listeners)
            : base(serializer, listeners)
        {
        }

        protected override object ToCache(object id, Type concreteType, object document, string json, UnitOfWorkOrigin origin)
        {
            return document;
        }

        protected override T FromCache<T>(object cacheValue)
        {
            return cacheValue.As<T>();
        }
    }
}