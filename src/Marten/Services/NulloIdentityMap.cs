using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;

namespace Marten.Services
{
    public class NulloIdentityMap : IIdentityMap
    {
        private readonly IEnumerable<IDocumentSessionListener> _listeners;

        public NulloIdentityMap(ISerializer serializer, IEnumerable<IDocumentSessionListener> listeners = null)
        {
            Serializer = serializer;
            _listeners = listeners ?? new IDocumentSessionListener[] { };
        }

        public ISerializer Serializer { get; }

        public T Get<T>(object id, TextReader json, Guid? version)
        {
            return Get<T>(id, typeof(T), json, version);
        }

        public T Get<T>(object id, Type concreteType, TextReader json, Guid? version)
        {
            if (version.HasValue)
                Versions.Store<T>(id, version.Value);

            var document = Serializer.FromJson(concreteType, json).As<T>();

            _listeners.Each(listener => listener.DocumentLoaded(id, document));

            return document;
        }

        public void Remove<T>(object id)
        {
            // nothing
        }

        public void RemoveAllOfType(Type type)
        {
            // nothing
        }

        public void Store<T>(object id, T entity, Guid? version = null)
        {
            if (version.HasValue)
                Versions.Store<T>(id, version.Value);
        }

        public bool Has<T>(object id)
        {
            return false;
        }

        public T Retrieve<T>(object id)
        {
            return default(T);
        }

        public IIdentityMap ForQuery()
        {
            return new IdentityMap(Serializer, Enumerable.Empty<IDocumentSessionListener>())
            {
                Versions = Versions
            };
        }

        public VersionTracker Versions { get; } = new VersionTracker();

        public void ClearChanges()
        {
        }
    }
}