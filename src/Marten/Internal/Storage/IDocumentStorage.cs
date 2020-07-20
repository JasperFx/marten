using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Linq;
using Marten.Internal.Operations;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Schema;
using Marten.Storage;
using Remotion.Linq;

namespace Marten.Internal.Storage
{
    public interface IDocumentStorage : ISelectClause
    {
        Type SourceType { get; }

        IFieldMapping Fields { get; }

        IWhereFragment FilterDocuments(QueryModel model, IWhereFragment query);

        IWhereFragment DefaultWhereFragment();

        IQueryableDocument QueryableDocument { get; }
        bool UseOptimisticConcurrency { get; }
    }

    public interface IDocumentStorage<T> : IDocumentStorage
    {
        object IdentityFor(T document);

        Type IdType { get; }
        Guid? VersionFor(T document, IMartenSession session);

        void Store(IMartenSession session, T document);
        void Store(IMartenSession session, T document, Guid? version);

        void Eject(IMartenSession session, T document);

        IStorageOperation Update(T document, IMartenSession session, ITenant tenant);
        IStorageOperation Insert(T document, IMartenSession session, ITenant tenant);
        IStorageOperation Upsert(T document, IMartenSession session, ITenant tenant);

        IStorageOperation Overwrite(T document, IMartenSession session, ITenant tenant);


        IStorageOperation DeleteForDocument(T document);


        IStorageOperation DeleteForWhere(IWhereFragment where);
        void EjectById(IMartenSession session, object id);
        void RemoveDirtyTracker(IMartenSession session, object id);
    }

    public interface IDocumentStorage<T, TId> : IDocumentStorage<T>
    {
        IStorageOperation DeleteForId(TId id);

        T Load(TId id, IMartenSession session);
        Task<T> LoadAsync(TId id, IMartenSession session, CancellationToken token);

        IReadOnlyList<T> LoadMany(TId[] ids, IMartenSession session);
        Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IMartenSession session, CancellationToken token);


        TId AssignIdentity(T document, ITenant tenant);
        TId Identity(T document);
    }
}
