using System;
using Baseline;
using Marten.Util;

namespace Marten.V4Internals
{
    public class StorageCheckingPersistenceGraph: IPersistenceGraph
    {
        private ImHashMap<Type, object> _storage = ImHashMap<Type, object>.Empty;
        private readonly ITenantStorage _tenant;
        private readonly IPersistenceGraph _inner;

        public StorageCheckingPersistenceGraph(ITenantStorage tenant, IPersistenceGraph inner)
        {
            _tenant = tenant;
            _inner = inner;
        }

        public DocumentPersistence<T> StorageFor<T>()
        {
            if (_storage.TryFind(typeof(T), out var stored))
            {
                return stored.As<DocumentPersistence<T>>();
            }

            _tenant.EnsureStorageExists(typeof(T));
            var persistence = _inner.StorageFor<T>();

            _storage = _storage.AddOrUpdate(typeof(T), persistence);

            return persistence;
        }
    }
}
