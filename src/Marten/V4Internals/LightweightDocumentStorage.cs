using System;
using Marten.Schema;

namespace Marten.V4Internals
{
    public abstract class LightweightDocumentStorage<T, TId>: DocumentStorage<T, TId>
    {
        public LightweightDocumentStorage(IQueryableDocument document) : base(document)
        {
        }

        public sealed override void Store(IMartenSession session, T document)
        {
            // TODO -- assign the identity here!
        }

        public sealed override void Store(IMartenSession session, T document, Guid? version)
        {
            var identity = Identity(document);

            if (version.HasValue)
            {
                session.Versions.StoreVersion<T, TId>(identity, version.Value);
            }
            else
            {
                session.Versions.ClearVersion<T, TId>(identity);
            }


        }

        public sealed override void Eject(IMartenSession session, T document)
        {
            // Nothing!
        }
    }
}
