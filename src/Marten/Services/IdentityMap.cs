using System;
using System.Collections.Concurrent;
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


        public T Get<T>(object id, Func<string> json) where T : class
        {
            return _objects[typeof (T)].GetOrAdd(id.GetHashCode(), _ =>
            {
                var text = json();
                if (text.IsEmpty()) return null;

                return _serializer.FromJson<T>(text);
            }).As<T>();
        }

        public T Get<T>(object id, string json) where T : class
        {
            return _objects[typeof(T)].GetOrAdd(id.GetHashCode(), _ =>
            {
                return _serializer.FromJson<T>(json);
            }).As<T>();
        }

        public void Remove<T>(object id)
        {
            object value;
            _objects[typeof (T)].TryRemove(id.GetHashCode(), out value);
        }

        public void Store<T>(object id, T entity)
        {
            _objects[typeof (T)].AddOrUpdate(id.GetHashCode(), entity, (i, e) => e);
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
    }
}