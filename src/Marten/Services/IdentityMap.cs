using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Services
{
    public class IdentityMap : IIdentityMap
    {
        private readonly ISerializer _serializer;

        private readonly Cache<Type, ConcurrentDictionary<int, object>> _objects 
            = new Cache<Type, ConcurrentDictionary<int, object>>(_ => new ConcurrentDictionary<int, object>());

        public IdentityMap(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public T Get<T>(object id, Func<FetchResult<T>> result) where T : class
        {
            return _objects[typeof (T)].GetOrAdd(id.GetHashCode(), _ => result()?.Document).As<T>();
        }

        public async Task<T> GetAsync<T>(object id, Func<CancellationToken, Task<FetchResult<T>>> result, CancellationToken token = default(CancellationToken)) where T : class
        {
            var dict = _objects[typeof (T)];
            var hashCode = id.GetHashCode();

            if (dict.ContainsKey(hashCode))
            {
                return dict[hashCode].As<T>();
            }

            var fetchResult = await result(token).ConfigureAwait(false);
            if (fetchResult == null) return null;

            dict[hashCode] = fetchResult.Document;

            return fetchResult.Document;
        }

        public T Get<T>(object id, string json) where T : class
        {
            return _objects[typeof(T)].GetOrAdd(id.GetHashCode(), _ =>
            {
                return Deserialize<T>(json);
            }).As<T>();
        }

        public T Get<T>(object id, Type concreteType, string json) where T : class
        {
            return (T) _objects[typeof (T)].GetOrAdd(id.GetHashCode(), _ =>
            {
                return json.IsEmpty() ? null : _serializer.FromJson(concreteType, json);
            });
        }

        public void Remove<T>(object id)
        {
            object value;
            _objects[typeof (T)].TryRemove(id.GetHashCode(), out value);
        }

        public void Store<T>(object id, T entity)
        {
            var dictionary = _objects[typeof (T)];
            var hashCode = id.GetHashCode();
            if (dictionary.ContainsKey(hashCode) && dictionary[hashCode] != null)
            {
                if (!ReferenceEquals(dictionary[hashCode], entity))
                {
                    throw new InvalidOperationException(
                        $"Document '{typeof(T).FullName}' with same Id already added to the session.");
                }
            }
            
            dictionary.AddOrUpdate(hashCode, entity, (i, e) => e);
        }

        public bool Has<T>(object id)
        {
            var dict = _objects[typeof (T)];
            var hashCode = id.GetHashCode();
            return dict.ContainsKey(hashCode) && dict[hashCode] != null;
        }

        public T Retrieve<T>(object id) where T : class
        {
            var dict = _objects[typeof(T)];
            var hashCode = id.GetHashCode();
            return dict.ContainsKey(hashCode) ? dict[hashCode] as T : null;
        }

        private T Deserialize<T>(string text) where T : class
        {
            if (text.IsEmpty())
            {
                return null;
            }

            return _serializer.FromJson<T>(text);
        }
    }
}