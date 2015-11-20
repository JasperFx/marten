using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using FubuCore;
using FubuCore.Util;

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

        public T Get<T>(object id, Func<string> json)
        {
            return _objects[typeof(T)].GetOrAdd(id.GetHashCode(), _ => new TrackedEntity(id, _serializer, typeof(T), json())).Document.As<T>();
        }

        public T Get<T>(object id, string json)
        {
            return _objects[typeof(T)].GetOrAdd(id.GetHashCode(), _ => new TrackedEntity(id, _serializer, typeof(T), json)).Document.As<T>();
        }

        public IEnumerable<DocumentChange> DetectChanges()
        {
            return _objects.SelectMany(x => x.Values.Select(_ => _.DetectChange())).Where(x => x != null).ToArray();
        }
    }
}