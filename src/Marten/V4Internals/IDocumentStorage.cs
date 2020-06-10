using System;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Schema;
using Remotion.Linq;

namespace Marten.V4Internals
{
    public interface IDocumentStorage<T> : ISelectClause
    {
        IFieldMapping Fields { get; }

        Type IdType { get; }
        Guid? VersionFor(T document, IMartenSession session);

        void Store(IMartenSession session, T document);
        void Store(IMartenSession session, T document, Guid? version);

        void Eject(IMartenSession session, T document);

        IStorageOperation Update(T document, IMartenSession session);
        IStorageOperation Insert(T document, IMartenSession session);
        IStorageOperation Upsert(T document, IMartenSession session);
        IStorageOperation Overwrite(T document, IMartenSession session);


        IStorageOperation DeleteForDocument(T document);


        IStorageOperation DeleteForWhere(IWhereFragment where);


        IWhereFragment FilterDocuments(QueryModel model, IWhereFragment query);

        IWhereFragment DefaultWhereFragment();
    }

    public abstract class DirtyTrackingDocumentStorage<T, TId>: DocumentStorage<T, TId>
    {
        public DirtyTrackingDocumentStorage(IQueryableDocument document): base(document)
        {
        }
    }
}
