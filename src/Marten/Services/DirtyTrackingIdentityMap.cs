using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Services
{
    public class DirtyTrackingIdentityMap : IdentityMap<TrackedEntity>, IDocumentTracker
    {
        readonly CharArrayTextWriter.IPool _pool;

        public DirtyTrackingIdentityMap(ISerializer serializer, IEnumerable<IDocumentSessionListener> listeners, CharArrayTextWriter.IPool pool) : base(serializer, listeners)
        {
            _pool = pool;
        }

        // TEST PURPOSES ONLY
        public DirtyTrackingIdentityMap(ISerializer serializer, IEnumerable<IDocumentSessionListener> listeners) : this(serializer, listeners, new CharArrayTextWriter.Pool())
        {}

        protected override TrackedEntity ToCache(object id, Type concreteType, object document, string json, UnitOfWorkOrigin origin)
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
            return Cache.SelectMany(x => x.Values.Where(_ => _.Origin == UnitOfWorkOrigin.Loaded).Select(_ => _.DetectChange())).Where(x => x != null).ToArray();
        }
    }
}