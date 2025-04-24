#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using JasperFx.Events.Aggregation;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Schema.BulkLoading;
using Marten.Services;
using Marten.Storage;
using Marten.Storage.Metadata;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Internal.Storage;

public interface IDocumentStorage: ISelectClause
{
    Type SourceType { get; }

    Type IdType { get; }

    bool UseOptimisticConcurrency { get; }
    IOperationFragment DeleteFragment { get; }
    IOperationFragment HardDeleteFragment { get; }
    IReadOnlyList<DuplicatedField> DuplicatedFields { get; }
    DbObjectName TableName { get; }
    Type DocumentType { get; }

    TenancyStyle TenancyStyle { get; }
    Task TruncateDocumentStorageAsync(IMartenDatabase database, CancellationToken ct = default);

    ISqlFragment FilterDocuments(ISqlFragment query, IMartenSession session);

    ISqlFragment? DefaultWhereFragment();

    IQueryableMemberCollection QueryMembers { get; }

    /// <summary>
    /// Necessary (maybe) for usage within the temporary tables when using Includes()
    /// </summary>
    ISelectClause SelectClauseWithDuplicatedFields { get; }

    bool UseNumericRevisions { get; }

    object RawIdentityValue(object id);
}

internal class CreateFromDocumentMapping: Variable
{
    public CreateFromDocumentMapping(DocumentMapping mapping, Type openType, GeneratedType type): base(
        openType.MakeGenericType(mapping.DocumentType), $"new {type.TypeName}(mapping)")
    {
    }
}

public class DocumentProvider<T> where T : notnull
{
    public DocumentProvider(IBulkLoader<T>? bulkLoader, IDocumentStorage<T> queryOnly, IDocumentStorage<T> lightweight,
        IDocumentStorage<T> identityMap, IDocumentStorage<T> dirtyTracking)
    {
        BulkLoader = bulkLoader;
        QueryOnly = queryOnly;
        Lightweight = lightweight;
        IdentityMap = identityMap;
        DirtyTracking = dirtyTracking;
    }

    public IBulkLoader<T>? BulkLoader { get; }
    public IDocumentStorage<T> QueryOnly { get; }
    public IDocumentStorage<T> Lightweight { get; }
    public IDocumentStorage<T> IdentityMap { get; }
    public IDocumentStorage<T> DirtyTracking { get; }

    public IDocumentStorage<T> Select(DocumentTracking tracking)
    {
        switch (tracking)
        {
            case DocumentTracking.None:
                return Lightweight;
            case DocumentTracking.QueryOnly:
                return QueryOnly;
            case DocumentTracking.DirtyTracking:
                return DirtyTracking;
            case DocumentTracking.IdentityOnly:
                return IdentityMap;

            default:
                throw new ArgumentOutOfRangeException(nameof(tracking));
        }
    }
}

public interface IDocumentStorage<T>: IDocumentStorage where T : notnull
{
    object IdentityFor(T document);


    Guid? VersionFor(T document, IMartenSession session);

    void Store(IMartenSession session, T document);
    void Store(IMartenSession session, T document, Guid? version);
    void Store(IMartenSession session, T document, int revision);

    void Eject(IMartenSession session, T document);

    IStorageOperation Update(T document, IMartenSession session, string tenantId);
    IStorageOperation Insert(T document, IMartenSession session, string tenantId);
    IStorageOperation Upsert(T document, IMartenSession session, string tenantId);

    IStorageOperation Overwrite(T document, IMartenSession session, string tenantId);

    IDeletion DeleteForDocument(T document, string tenantId);


    void EjectById(IMartenSession session, object id);
    void RemoveDirtyTracker(IMartenSession session, object id);
    IDeletion HardDeleteForDocument(T document, string tenantId);

    void SetIdentityFromString(T document, string identityString);
    void SetIdentityFromGuid(T document, Guid identityGuid);
}

public interface IDocumentStorage<T, TId>: IDocumentStorage<T>, IIdentitySetter<T, TId> where T : notnull where TId : notnull
{
    IDeletion DeleteForId(TId id, string tenantId);

    Task<T?> LoadAsync(TId id, IMartenSession session, CancellationToken token);

    Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IMartenSession session, CancellationToken token);


    TId AssignIdentity(T document, string tenantId, IMartenDatabase database);
    TId Identity(T document);
    ISqlFragment ByIdFilter(TId id);
    IDeletion HardDeleteForId(TId id, string tenantId);
    NpgsqlCommand BuildLoadCommand(TId id, string tenantId);
    NpgsqlCommand BuildLoadManyCommand(TId[] ids, string tenantId);
    object RawIdentityValue(TId id);
}

internal static class DocumentStoreExtensions
{
    public static void AddTenancyFilter(this IDocumentStorage storage, ICommandBuilder sql, string tenantId)
    {
        if (storage.TenancyStyle == TenancyStyle.Conjoined)
        {
            sql.Append(" and ");
            sql.Append("d.");
            sql.Append(TenantIdColumn.Name);
            sql.Append(" = ");
            sql.AppendParameter(tenantId);
        }
    }
}
