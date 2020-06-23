using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.V4Internals.Linq;
using Remotion.Linq;

namespace Marten.V4Internals
{
    public interface IDocumentStorage : ISelectClause
    {
        Type SourceType { get; }

        IFieldMapping Fields { get; }

        IWhereFragment FilterDocuments(QueryModel model, IWhereFragment query);

        IWhereFragment DefaultWhereFragment();
    }

    public interface IDocumentStorage<T> : IDocumentStorage
    {
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

    }

    public interface IDocumentStorage<T, TId> : IDocumentStorage<T>
    {
        IStorageOperation DeleteForId(TId id);

        T Load(TId id, IMartenSession session);
        Task<T> LoadAsync(TId id, IMartenSession session, CancellationToken token);

        IReadOnlyList<T> LoadMany(TId[] ids, IMartenSession session);
        Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IMartenSession session, CancellationToken token);

    }
}
