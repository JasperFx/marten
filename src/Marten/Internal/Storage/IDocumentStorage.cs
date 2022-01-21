using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Internal.Operations;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Schema.BulkLoading;
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
        Task TruncateDocumentStorageAsync(IMartenDatabase database);
        void TruncateDocumentStorage(IMartenDatabase database);

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

    internal class CreateFromDocumentMapping: Variable
    {
        public CreateFromDocumentMapping(DocumentMapping mapping, Type openType, GeneratedType type) : base(openType.MakeGenericType(mapping.DocumentType), $"new {type.TypeName}(mapping)")
        {
        }
    }

    public class DocumentProvider<T> where T : notnull
    {
        public DocumentProvider(IBulkLoader<T> bulkLoader, IDocumentStorage<T> queryOnly, IDocumentStorage<T> lightweight, IDocumentStorage<T> identityMap, IDocumentStorage<T> dirtyTracking)
        {
            BulkLoader = bulkLoader;
            QueryOnly = queryOnly;
            Lightweight = lightweight;
            IdentityMap = identityMap;
            DirtyTracking = dirtyTracking;
        }

        public IBulkLoader<T> BulkLoader { get; }
        public IDocumentStorage<T> QueryOnly { get; }
        public IDocumentStorage<T> Lightweight { get; }
        public IDocumentStorage<T> IdentityMap { get; }
        public IDocumentStorage<T> DirtyTracking { get; }
    }

    public interface IDocumentStorage<T> : IDocumentStorage where T : notnull
    {
        object IdentityFor(T document);


        Guid? VersionFor(T document, IMartenSession session);

        void Store(IMartenSession session, T document);
        void Store(IMartenSession session, T document, Guid? version);

        void Eject(IMartenSession session, T document);

        IStorageOperation Update(T document, IMartenSession session, string tenantId);
        IStorageOperation Insert(T document, IMartenSession session, string tenantId);
        IStorageOperation Upsert(T document, IMartenSession session, string tenantId);

        IStorageOperation Overwrite(T document, IMartenSession session, string tenantId);

        IDeletion DeleteForDocument(T document, string tenantId);


        void EjectById(IMartenSession session, object id);
        void RemoveDirtyTracker(IMartenSession session, object id);
        IDeletion HardDeleteForDocument(T document, string tenantId);
    }

    public interface IDocumentStorage<T, TId> : IDocumentStorage<T> where T : notnull where TId : notnull
    {
        /// <summary>
        /// Assign the given identity to the document
        /// </summary>
        /// <param name="document"></param>
        /// <param name="identity"></param>
        void SetIdentity(T document, TId identity);

        IDeletion DeleteForId(TId id, string tenantId);

        T? Load(TId id, IMartenSession session);
        Task<T?> LoadAsync(TId id, IMartenSession session, CancellationToken token);

        IReadOnlyList<T> LoadMany(TId[] ids, IMartenSession session);
        Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IMartenSession session, CancellationToken token);


        TId AssignIdentity(T document, string tenantId, IMartenDatabase database);
        TId Identity(T document);
        ISqlFragment ByIdFilter(TId id);
        IDeletion HardDeleteForId(TId id, string tenantId);
        NpgsqlCommand BuildLoadCommand(TId id, string tenantId);
        NpgsqlCommand BuildLoadManyCommand(TId[] ids, string tenantId);
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
