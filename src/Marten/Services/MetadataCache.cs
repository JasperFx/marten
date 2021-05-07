using Marten.Storage;
using Marten.Util;
using System;
using Baseline.ImTools;
using Marten.Storage.Metadata;

namespace Marten.Services
{
    public class MetadataCache
    {
        protected Ref<ImHashMap<Type, ImHashMap<object, DocumentMetadata>>> Cache { get; } = Ref.Of(ImHashMap<Type, ImHashMap<object, DocumentMetadata>>.Empty);

        public DocumentMetadata MetadataFor<T>(object id)
        {
            return MetadataFor(typeof(T), id);
        }

        public DocumentMetadata MetadataFor(Type documentType, object id)
        {
            if (Cache.Value.TryFind(documentType, out var t) && t.TryFind(id, out var value))
            {
                return value;
            }

            return null;
        }

        public void Store<T>(object id, DocumentMetadata metadata)
        {
            if (metadata == null)
            {
                return;
            }

            Store(typeof(T), id, metadata);
        }

        public void Store(Type documentType, object id, DocumentMetadata metadata)
        {
            Cache.Swap(c => c.AddOrUpdate(documentType,
                (c.GetValueOrDefault(documentType) ?? ImHashMap<object, DocumentMetadata>.Empty).AddOrUpdate(id, metadata)));
        }

        public void Remove<T>(object id)
        {
            Remove(typeof(T), id);
        }

        public void Remove(Type documentType, object id)
        {
            Cache.Swap(c => c.AddOrUpdate(documentType,
                (c.GetValueOrDefault(documentType) ?? ImHashMap<object, DocumentMetadata>.Empty).Remove(id)));
        }

        public void RemoveAllOfType(Type documentType)
        {
            Cache.Swap(c => c.AddOrUpdate(documentType, ImHashMap<object, DocumentMetadata>.Empty));
        }
    }
}
