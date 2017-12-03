using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        protected IdentityMap(ISerializer serializer, IEnumerable<IDocumentSessionListener> listeners)
        {
            Serializer = serializer;
            _listeners = listeners ?? new IDocumentSessionListener[] { };
        }

        protected ConcurrentCache<Type, ConcurrentDictionary<object, TCacheValue>> Cache { get; }
            = new ConcurrentCache<Type, ConcurrentDictionary<object, TCacheValue>>(
                _ => new ConcurrentDictionary<object, TCacheValue>());

        public ISerializer Serializer { get; }

        public T Get<T>(object id, TextReader json, Guid? version)
        {
            return Get<T>(id, typeof(T), json, version);
        }

        public T Get<T>(object id, Type concreteType, TextReader json, Guid? version)
        {
            var cacheValue = Cache[typeof(T)].GetOrAdd(id, _ =>
            {
                if (version.HasValue)
                    Versions.Store<T>(id, version.Value);

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

        public void RemoveAllOfType(Type type)
        {
            if (Cache.Has(type))
                Cache[type].Clear();
        }

        public void Store<T>(object id, T entity, Guid? version = null)
        {
            if (version.HasValue)
                Versions.Store<T>(id, version.Value);

            var dictionary = Cache[typeof(T)];

            if (dictionary.ContainsKey(id) && dictionary[id] != null)
            {
                var existing = FromCache<T>(dictionary[id]);
                if (existing != null && !ReferenceEquals(existing, entity))
                    throw new InvalidOperationException(
                        $"Document '{typeof(T).FullName}' with same Id already added to the session.");
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

        public virtual void ClearChanges()
        {
        }

        protected IEnumerable<TCacheValue> allCachedValues()
        {
            return Cache.SelectMany(x => x.Values);
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