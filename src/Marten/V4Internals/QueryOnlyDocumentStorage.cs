using System;
using Marten.Schema;

namespace Marten.V4Internals
{
    public abstract class QueryOnlyDocumentStorage<T, TId>: DocumentStorage<T, TId>
    {
        public QueryOnlyDocumentStorage(IQueryableDocument document) : base(document)
        {
        }

        public sealed override void Store(IMartenSession session, T document)
        {
            // Nothing
        }

        public sealed override void Store(IMartenSession session, T document, Guid? version)
        {
            // Nothing
        }

        public sealed override void Eject(IMartenSession session, T document)
        {
            // Nothing
        }
    }
}
