using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Operations;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Schema.Arguments;
using Weasel.Postgresql;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using Remotion.Linq;
using Weasel.Core;
using Weasel.Postgresql.SqlGeneration;

#nullable enable
namespace Marten.Internal.Storage
{
    public interface IDocumentStorage : ISelectClause
    {
        Task TruncateDocumentStorageAsync(ITenant tenant);
        void TruncateDocumentStorage(ITenant tenant);

        Type SourceType { get; }

        Type IdType { get; }

        IFieldMapping Fields { get; }

        ISqlFragment FilterDocuments(QueryModel? model, ISqlFragment query);

        ISqlFragment? DefaultWhereFragment();

        bool UseOptimisticConcurrency { get; }
        IOperationFragment DeleteFragment { get; }
        IOperationFragment HardDeleteFragment { get; }
        DuplicatedField[] DuplicatedFields { get; }
        DbObjectName TableName { get; }
        Type DocumentType { get; }

        TenancyStyle TenancyStyle { get; }

    }

    public interface IDocumentStorage<T> : IDocumentStorage where T : notnull
    {
        object IdentityFor(T document);


        Guid? VersionFor(T document, IMartenSession session);

        void Store(IMartenSession session, T document);
        void Store(IMartenSession session, T document, Guid? version);

        void Eject(IMartenSession session, T document);

        IStorageOperation Update(T document, IMartenSession session, ITenant tenant);
        IStorageOperation Insert(T document, IMartenSession session, ITenant tenant);
        IStorageOperation Upsert(T document, IMartenSession session, ITenant tenant);

        IStorageOperation Overwrite(T document, IMartenSession session, ITenant tenant);

        IDeletion DeleteForDocument(T document, ITenant tenant);


        void EjectById(IMartenSession session, object id);
        void RemoveDirtyTracker(IMartenSession session, object id);
        IDeletion HardDeleteForDocument(T document, ITenant tenant);
    }

    public interface IDocumentStorage<T, TId> : IDocumentStorage<T> where T : notnull where TId : notnull
    {
        /// <summary>
        /// Assign the given identity to the document
        /// </summary>
        /// <param name="document"></param>
        /// <param name="identity"></param>
        void SetIdentity(T document, TId identity);

        IDeletion DeleteForId(TId id, ITenant tenant);

        T? Load(TId id, IMartenSession session);
        Task<T?> LoadAsync(TId id, IMartenSession session, CancellationToken token);

        IReadOnlyList<T> LoadMany(TId[] ids, IMartenSession session);
        Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IMartenSession session, CancellationToken token);


        TId AssignIdentity(T document, ITenant tenant);
        TId Identity(T document);
        ISqlFragment ByIdFilter(TId id);
        IDeletion HardDeleteForId(TId id, ITenant tenant);
        NpgsqlCommand BuildLoadCommand(TId id, ITenant tenant);
        NpgsqlCommand BuildLoadManyCommand(TId[] ids, ITenant tenant);
    }

    internal static class DocumentStoreExtensions
    {
        public static void AddTenancyFilter(this IDocumentStorage storage, CommandBuilder sql)
        {
            if (storage.TenancyStyle == TenancyStyle.Conjoined)
            {
                sql.Append($" and {CurrentTenantFilter.Filter}");
                sql.AddNamedParameter(TenantIdArgument.ArgName, "");
            }
        }

    }

}
