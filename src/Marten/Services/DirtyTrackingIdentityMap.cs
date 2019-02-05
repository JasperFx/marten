using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Marten.Services
{
    public class DirtyTrackingIdentityMap : IdentityMap<TrackedEntity>, IDocumentTracker
    {
        public DirtyTrackingIdentityMap(ISerializer serializer, IEnumerable<IDocumentSessionListener> listeners) : base(serializer, listeners)
        {
        }

        protected override TrackedEntity ToCache(object id, Type concreteType, object document, TextReader json, UnitOfWorkOrigin origin = UnitOfWorkOrigin.Loaded)
        {
            return new TrackedEntity(id, Serializer, concreteType, document, json)
            {
                Origin = origin
            };
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
            return Cache.Value.Enumerate().SelectMany(x => x.Value.Enumerate().Where(_ => _.Value.Origin == UnitOfWorkOrigin.Loaded).Select(_ => _.Value.DetectChange())).Where(x => x != null).ToArray();
        }

        public override void ClearChanges()
        {
            foreach (var trackedEntity in allCachedValues())
            {
                trackedEntity.Origin = UnitOfWorkOrigin.Loaded;
            }
        }
    }
}