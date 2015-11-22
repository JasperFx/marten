using System;
using System.Collections.Concurrent;
using FubuCore;
using FubuCore.Util;

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


        public T Get<T>(object id, Func<string> json)
        {
            return _objects[typeof (T)].GetOrAdd(id.GetHashCode(), _ =>
            {
                return _serializer.FromJson<T>(json());
            }).As<T>();
        }

        public T Get<T>(object id, string json)
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
    }
}