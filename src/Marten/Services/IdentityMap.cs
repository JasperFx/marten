using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Services
{
    public abstract class IdentityMap<TCacheValue> : IIdentityMap
    {
        private readonly IEnumerable<IDocumentSessionListener> _listeners;

        protected Cache<Type, ConcurrentDictionary<object, TCacheValue>> Cache { get; }
            = new Cache<Type, ConcurrentDictionary<object, TCacheValue>>(_ => new ConcurrentDictionary<object, TCacheValue>());

        public ISerializer Serializer { get; }

        protected IdentityMap(ISerializer serializer, IEnumerable<IDocumentSessionListener> listeners)
        {
            Serializer = serializer;
            _listeners = listeners ?? new IDocumentSessionListener[] { };
        }

        protected abstract TCacheValue ToCache(object id, Type concreteType, object document, string json);
        protected abstract T FromCache<T>(TCacheValue cacheValue) where T : class;

        public T Get<T>(object id, Func<FetchResult<T>> result) where T : class
        {
            var cacheValue = Cache[typeof(T)].GetOrAdd(id, _ =>
            {
                var fetchResult = result();
                var document = fetchResult?.Document;
                _listeners.Each(listener => listener.DocumentLoaded(id, document));
                return ToCache(id, typeof(T), document, fetchResult?.Json);
            });
            return FromCache<T>(cacheValue);
        }

        public async Task<T> GetAsync<T>(object id, Func<CancellationToken, Task<FetchResult<T>>> result, CancellationToken token = default(CancellationToken)) where T : class
        {
            var dictionary = Cache[typeof(T)];

            if (dictionary.ContainsKey(id))
            {
                return FromCache<T>(dictionary[id]);
            }

            var fetchResult = await result(token).ConfigureAwait(false);
            if (fetchResult == null) return null;

            var document = fetchResult.Document;

            dictionary[id] = ToCache(id, typeof(T), document, fetchResult.Json);

            _listeners.Each(listener => listener.DocumentLoaded(id, document));

            return document;
        }

        public T Get<T>(object id, string json, Guid? version) where T : class
        {
            return Get<T>(id, typeof(T), json, version);
        }

        public T Get<T>(object id, Type concreteType, string json, Guid? version) where T : class
        {
            var cacheValue = Cache[typeof(T)].GetOrAdd(id, _ =>
            {
                if (json.IsEmpty()) return ToCache(id, concreteType, null, json);

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

        public void Store<T>(object id, T entity) where T : class
        {
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

            var cacheValue = ToCache(id, typeof(T), entity, null);
            dictionary.AddOrUpdate(id, cacheValue, (i, e) => e);
        }

        public bool Has<T>(object id) where T : class
        {
            var dict = Cache[typeof(T)];
            return dict.ContainsKey(id) && FromCache<T>(dict[id]) != null;
        }

        public T Retrieve<T>(object id) where T : class
        {
            var dict = Cache[typeof(T)];
            return dict.ContainsKey(id) ? FromCache<T>(dict[id]): null;
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

        protected override object ToCache(object id, Type concreteType, object document, string json)
        {
            return document;
        }

        protected override T FromCache<T>(object cacheValue)
        {
            return cacheValue.As<T>();
        }
    }
}