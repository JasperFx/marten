using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

        public T Get<T>(object id, Func<string> json) where T : class
        {
            return _objects[typeof(T)].GetOrAdd(id.GetHashCode(), _ =>
            {
                return new TrackedEntity(id, _serializer, typeof (T), json());
            }).Document as T;
        }

        public T Get<T>(object id, string json) where T : class
        {
            return _objects[typeof(T)].GetOrAdd(id.GetHashCode(), _ => new TrackedEntity(id, _serializer, typeof(T), json)).Document.As<T>();
        }

        public void Remove<T>(object id)
        {
            TrackedEntity value;
            _objects[typeof(T)].TryRemove(id.GetHashCode(), out value);
        }

        public void Store<T>(object id, T entity)
        {
            _objects[typeof(T)].AddOrUpdate(id.GetHashCode(), new TrackedEntity(id, _serializer, typeof(T), entity), (i, e) => e);
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