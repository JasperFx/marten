using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Util;

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

        protected IdentityMap(ISerializer serializer, IEnumerable<IDocumentSessionListener> listeners)
        {
            Serializer = serializer;
            _listeners = listeners ?? new IDocumentSessionListener[] { };
        }

        protected Ref<ImHashMap<Type, ImHashMap<object, TCacheValue>>> Cache { get; }
            = Ref.Of(ImHashMap<Type, ImHashMap<object, TCacheValue>>.Empty);

        public ISerializer Serializer { get; }

        public T Get<T>(object id, TextReader json, Guid? version)
        {
            return Get<T>(id, typeof(T), json, version);
        }

        public T Get<T>(object id, Type concreteType, TextReader json, Guid? version)
        {

            if (Cache.Value.TryFind(typeof(T), out var t) && t.TryFind(id, out var value))
            {
                return FromCache<T>(value);
            }

            if (version.HasValue)
                Versions.Store<T>(id, version.Value);

            var document = Serializer.FromJson(concreteType, json);

            _listeners.Each(listener => listener.DocumentLoaded(id, document));

            var cacheValue = ToCache(id, concreteType, document, json);

            Cache.Swap(c => c.AddOrUpdate(typeof(T),
                (c.GetValueOrDefault(typeof(T)) ?? ImHashMap<object, TCacheValue>.Empty).AddOrUpdate(id, cacheValue)));

            return FromCache<T>(cacheValue);
        }

        public void Remove<T>(object id)
        {	        
            Cache.Swap(c => c.AddOrUpdate(typeof(T),
                (c.GetValueOrDefault(typeof(T)) ?? ImHashMap<object, TCacheValue>.Empty).Remove(id)));
        }

        public void RemoveAllOfType(Type type)
        {
            Cache.Swap(c => c.AddOrUpdate(type, ImHashMap<object, TCacheValue>.Empty));
        }

        public void Store<T>(object id, T entity, Guid? version = null)
        {
            if (version.HasValue)
                Versions.Store<T>(id, version.Value);
            
            if (Cache.Value.TryFind(typeof(T), out var dictionary) && dictionary.TryFind(id, out var value))
            {
                var existing = FromCache<T>(value);
                if (existing != null && !ReferenceEquals(existing, entity))
                    throw new InvalidOperationException(
                        $"Document '{typeof(T).FullName}' with same Id already added to the session.");
            }

            _listeners.Each(listener => listener.DocumentAddedForStorage(id, entity));

            var cacheValue = ToCache(id, typeof(T), entity, null, UnitOfWorkOrigin.Stored);
            
            Cache.Swap(c => c.AddOrUpdate(typeof(T),
                (c.GetValueOrDefault(typeof(T)) ?? ImHashMap<object, TCacheValue>.Empty).AddOrUpdate(id, cacheValue)));
        }

        public bool Has<T>(object id)
        {
            return Cache.Value.TryFind(typeof(T), out var dict) && dict.TryFind(id, out _);
        }

        public T Retrieve<T>(object id)
        {            
            if (Cache.Value.TryFind(typeof(T), out var dict) && dict.TryFind(id, out var value))
            {
                return FromCache<T>(value);
            }
            return default(T);
        }

        public IIdentityMap ForQuery()
        {
            return this;
        }

        public VersionTracker Versions { get; set; } = new VersionTracker();

        public virtual void ClearChanges()
        {
        }

        protected IEnumerable<TCacheValue> allCachedValues()
        {
            return Cache.Value.Enumerate().SelectMany(x => x.Value.Enumerate().Select(t => t.Value));
        }

        protected abstract TCacheValue ToCache(object id, Type concreteType, object document, TextReader json,
            UnitOfWorkOrigin origin = UnitOfWorkOrigin.Loaded);

        protected abstract T FromCache<T>(TCacheValue cacheValue);
    }

    public class IdentityMap : IdentityMap<object>
    {
        public IdentityMap(ISerializer serializer, IEnumerable<IDocumentSessionListener> listeners)
            : base(serializer, listeners)
        {
        }

        protected override object ToCache(object id, Type concreteType, object document, TextReader json,
            UnitOfWorkOrigin origin = UnitOfWorkOrigin.Loaded)
        {
            return document;
        }

        protected override T FromCache<T>(object cacheValue)
        {
            return cacheValue.As<T>();
        }
    }
}