using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Services
{
    public class IdentityMap : IIdentityMap
    {
        private readonly ISerializer _serializer;
        private readonly IEnumerable<IDocumentSessionListener> _listeners;

        private readonly Cache<Type, ConcurrentDictionary<object, object>> _objects
            = new Cache<Type, ConcurrentDictionary<object, object>>(_ => new ConcurrentDictionary<object, object>());

        public IdentityMap(ISerializer serializer, IEnumerable<IDocumentSessionListener> listeners)
        {
            _serializer = serializer;
            _listeners = listeners?.Any() == true ? listeners : null;
        }

        public T Get<T>(object id, Func<FetchResult<T>> result) where T : class
        {
            return _objects[typeof(T)].GetOrAdd(id, _ =>
            {
                var document = result()?.Document;
                _listeners?.Each(listener => listener.DocumentLoaded(id, document));
                return document;
            }).As<T>();
        }

        public ISerializer Serializer => _serializer;

        public async Task<T> GetAsync<T>(object id, Func<CancellationToken, Task<FetchResult<T>>> result, CancellationToken token = default(CancellationToken)) where T : class
        {
            var dict = _objects[typeof(T)];

            if (dict.ContainsKey(id))
            {
                return dict[id].As<T>();
            }

            var fetchResult = await result(token).ConfigureAwait(false);
            if (fetchResult == null) return null;

            dict[id] = fetchResult.Document;

            _listeners?.Each(listener => listener.DocumentLoaded(id, fetchResult.Document));

            return fetchResult.Document;
        }

        public T Get<T>(object id, string json) where T : class
        {
            return Get<T>(id, typeof (T), json);
        }

        public T Get<T>(object id, Type concreteType, string json) where T : class
        {
            return (T)_objects[typeof(T)].GetOrAdd(id, _ =>
            {
                if (json.IsEmpty()) return null;

                var document = _serializer.FromJson(concreteType, json);

                _listeners?.Each(listener => listener.DocumentLoaded(id, document));

                return document;
            });
        }

        public void Remove<T>(object id)
        {
            object value;
            _objects[typeof(T)].TryRemove(id, out value);
        }

        public void Store<T>(object id, T entity)
        {
            var dictionary = _objects[typeof(T)];

            if (dictionary.ContainsKey(id) && dictionary[id] != null)
            {
                if (!ReferenceEquals(dictionary[id], entity))
                {
                    throw new InvalidOperationException(
                        $"Document '{typeof(T).FullName}' with same Id already added to the session.");
                }
            }

            _listeners?.Each(listener => listener.DocumentAddedForStorage(id, entity));

            dictionary.AddOrUpdate(id, entity, (i, e) => e);
        }

        public bool Has<T>(object id)
        {
            var dict = _objects[typeof(T)];
            return dict.ContainsKey(id) && dict[id] != null;
        }

        public T Retrieve<T>(object id) where T : class
        {
            var dict = _objects[typeof(T)];
            return dict.ContainsKey(id) ? dict[id] as T : null;
        }
    }
}