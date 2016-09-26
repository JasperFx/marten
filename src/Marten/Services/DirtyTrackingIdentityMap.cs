using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Services
{
    public class DirtyTrackingIdentityMap : IdentityMap<TrackedEntity>, IDocumentTracker
    {
        public DirtyTrackingIdentityMap(ISerializer serializer, IEnumerable<IDocumentSessionListener> listeners) : base(serializer, listeners)
        {
        }

        protected override TrackedEntity ToCache(object id, Type concreteType, object document, string json)
        {
            return new TrackedEntity(id, Serializer, concreteType, document, json);
        }

        protected override T FromCache<T>(TrackedEntity cacheValue)
        {
            if (cacheValue == null)
            {
                return default(T);
            }

            return (T) cacheValue.Document;
        }

        public IEnumerable<DocumentChange> DetectChanges()
        {
            return Cache.SelectMany(x => x.Values.Select(_ => _.DetectChange())).Where(x => x != null).ToArray();
        }
    }
}