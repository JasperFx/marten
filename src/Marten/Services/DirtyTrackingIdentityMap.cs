using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Services
{
    public class DirtyTrackingIdentityMap : IIdentityMap, IDocumentTracker
    {
        private readonly ISerializer _serializer;

        private readonly Cache<Type, ConcurrentDictionary<int, TrackedEntity>> _objects
            = new Cache<Type, ConcurrentDictionary<int, TrackedEntity>>(_ => new ConcurrentDictionary<int, TrackedEntity>());


        public DirtyTrackingIdentityMap(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public T Get<T>(object id, Func<FetchResult<T>> result) where T : class
        {
            return _objects[typeof(T)].GetOrAdd(id.GetHashCode(), _ =>
            {
                var fetchResult = result();

                return new TrackedEntity(id, typeof(T), fetchResult?.Document, fetchResult?.Json, _serializer);
            }).Document as T;
        }

        public async Task<T> GetAsync<T>(object id, Func<CancellationToken, Task<FetchResult<T>>> result, CancellationToken token = default(CancellationToken)) where T : class
        {
            

            var dict = _objects[typeof (T)];
            var hashCode = id.GetHashCode();

            if (dict.ContainsKey(hashCode))
            {
                return dict[hashCode].Document.As<T>();
            }

            var fetchResult = await result(token).ConfigureAwait(false);
            if (fetchResult == null) return null;

            dict[hashCode] = new TrackedEntity(id, typeof(T), fetchResult.Document, fetchResult.Json, _serializer);

            return fetchResult?.Document;
        }

        public T Get<T>(object id, string json) where T : class
        {
            return _objects[typeof(T)].GetOrAdd(id.GetHashCode(), _ => new TrackedEntity(id, _serializer, typeof(T), json)).Document.As<T>();
        }

        public T Get<T>(object id, Type concreteType, string json) where T : class
        {
            return _objects[typeof(T)].GetOrAdd(id.GetHashCode(), _ => new TrackedEntity(id, _serializer, concreteType, json)).Document.As<T>();
        }

        public void Remove<T>(object id)
        {
            TrackedEntity value;
            _objects[typeof(T)].TryRemove(id.GetHashCode(), out value);
        }

        public void Store<T>(object id, T entity)
        {
            var dictionary = _objects[typeof(T)];
            var hashCode = id.GetHashCode();

            if (dictionary.ContainsKey(hashCode) && !ReferenceEquals(entity, dictionary[hashCode].Document))
            {
                throw new InvalidOperationException(
                    $"Document '{typeof(T).FullName}' with same Id already added to the session.");
            }

            dictionary.AddOrUpdate(hashCode, new TrackedEntity(id, _serializer, typeof(T), entity), (i, e) => e);
        }

        public IEnumerable<DocumentChange> DetectChanges()
        {
            return _objects.SelectMany(x => x.Values.Select(_ => _.DetectChange())).Where(x => x != null).ToArray();
        }

        public bool Has<T>(object id)
        {
            var hash = id.GetHashCode();
            var dict = _objects[typeof (T)];
            return dict.ContainsKey(hash) && dict[hash].Document != null;
        }

        public T Retrieve<T>(object id) where T : class
        {
            var hash = id.GetHashCode();
            var dict = _objects[typeof(T)];

            return dict.ContainsKey(hash) ? dict[hash].Document as T : null;
        }
    }
}