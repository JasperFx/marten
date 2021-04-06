using System;
using Baseline;
using Marten.Internal.CodeGeneration;
using Marten.Storage;
using Marten.Util;
#nullable enable
namespace Marten.Internal
{
    public class StorageCheckingProviderGraph: IProviderGraph
    {
        private ImHashMap<Type, object> _storage = ImHashMap<Type, object>.Empty;
        private readonly IProviderGraph _inner;

        public StorageCheckingProviderGraph(ITenantStorage tenant, IProviderGraph inner)
        {
            Tenant = tenant;
            _inner = inner;
        }

        public ITenantStorage Tenant { get; }

        public DocumentProvider<T> StorageFor<T>()
        {
            if (_storage.TryFind(typeof(T), out var stored))
            {
                return stored.As<DocumentProvider<T>>();
            }

            Tenant.EnsureStorageExists(typeof(T));
            var persistence = _inner.StorageFor<T>();

            _storage = _storage.AddOrUpdate(typeof(T), persistence);

            return persistence;
        }
    }
}
