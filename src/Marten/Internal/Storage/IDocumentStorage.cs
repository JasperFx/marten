using System;
using System.Data.Common;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
using System.Diagnostics.CodeAnalysis;

namespace Marten.Internal.Storage;

[UnconditionalSuppressMessage("AOT", "IL2055",
    Justification = "Class-level: Type.MakeGenericType with a runtime-determined type argument — AOT consumers must pre-register the closed shape they need (see the AOT publishing guide).")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public interface IDocumentStorage: ISelectClause
{
    Type SourceType { get; }

    Type IdType { get; }

    bool UseOptimisticConcurrency { get; }
    IOperationFragment DeleteFragment { get; }
    IOperationFragment HardDeleteFragment { get; }
    IReadOnlyList<IDuplicatedField> DuplicatedFields { get; }
    DbObjectName TableName { get; }
    Type DocumentType { get; }

    TenancyStyle TenancyStyle { get; }
    Task TruncateDocumentStorageAsync(IMartenDatabase database, CancellationToken ct = default);

    ISqlFragment FilterDocuments(ISqlFragment query, IStorageSession session);

    ISqlFragment? DefaultWhereFragment();

    IQueryableMemberCollection QueryMembers { get; }

    /// <summary>
    /// Necessary (maybe) for usage within the temporary tables when using Includes()
    /// </summary>
    ISelectClause SelectClauseWithDuplicatedFields { get; }

    bool UseNumericRevisions { get; }

    object RawIdentityValue(object id);
}

public class DocumentProvider<T> where T : notnull
{
    public DocumentProvider(IBulkLoader<T> bulkLoader, IDocumentStorage<T> queryOnly, IDocumentStorage<T> lightweight,
        IDocumentStorage<T> identityMap, IDocumentStorage<T> dirtyTracking)
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

    public IDocumentStorage<T> Select(DocumentTracking tracking)
    {
        return tracking switch
        {
            DocumentTracking.None => Lightweight,
            DocumentTracking.QueryOnly => QueryOnly,
            DocumentTracking.DirtyTracking => DirtyTracking,
            DocumentTracking.IdentityOnly => IdentityMap,
            _ => throw new ArgumentOutOfRangeException(nameof(tracking)),
        };
    }
}

public interface IDocumentStorage<T>: IDocumentStorage where T : notnull
{
    object IdentityFor(T document);


    Guid? VersionFor(T document, IStorageSession session);

    void Store(IStorageSession session, T document);
    void Store(IStorageSession session, T document, Guid? version);
    void Store(IStorageSession session, T document, long revision);

    void Eject(IStorageSession session, T document);

    IStorageOperation Update(T document, IStorageSession session, string tenantId);
    IStorageOperation Insert(T document, IStorageSession session, string tenantId);
    IStorageOperation Upsert(T document, IStorageSession session, string tenantId);

    IStorageOperation Overwrite(T document, IStorageSession session, string tenantId);

    /// <summary>
    /// Lighter-weight overwrite for projection storage. Builds the same Overwrite operation
    /// but does NOT consult session-level version / revision tracking, so it is safe to call
    /// from parallel async-daemon slice handlers that share an <see cref="IMartenSession"/>
    /// (see https://github.com/JasperFx/marten/issues/4657). The session is, by contract,
    /// not thread-safe; projections set the revision explicitly from the event and never
    /// read the session's <c>Versions</c> back, so there is no reason for the projection
    /// path to touch it.
    /// </summary>
    IStorageOperation OverwriteProjected(T document, string tenantId);

    /// <summary>
    /// Session-free Upsert for projection storage (#4667 Phase 1). Builds the same Upsert
    /// operation as <see cref="Upsert"/> but passes a null version/revision tracker so the
    /// projection path never touches <see cref="IMartenSession.Versions"/>. Safe to call
    /// from parallel async-daemon slice handlers that share an <see cref="IMartenSession"/>.
    /// </summary>
    IStorageOperation UpsertProjected(T document, string tenantId);

    /// <summary>
    /// Session-free Insert for projection storage (#4667 Phase 1). See
    /// <see cref="UpsertProjected"/>.
    /// </summary>
    IStorageOperation InsertProjected(T document, string tenantId);

    /// <summary>
    /// Session-free Update for projection storage (#4667 Phase 1). See
    /// <see cref="UpsertProjected"/>.
    /// </summary>
    IStorageOperation UpdateProjected(T document, string tenantId);

    IDeletion DeleteForDocument(T document, string tenantId);


    void EjectById(IStorageSession session, object id);
    void RemoveDirtyTracker(IStorageSession session, object id);
    IDeletion HardDeleteForDocument(T document, string tenantId);

    void SetIdentityFromString(T document, string identityString);
    void SetIdentityFromGuid(T document, Guid identityGuid);
}

public interface IDocumentStorage<T, TId>: IDocumentStorage<T>, IIdentitySetter<T, TId> where T : notnull where TId : notnull
{
    IDeletion DeleteForId(TId id, string tenantId);

    Task<T?> LoadAsync(TId id, IStorageSession session, CancellationToken token);

    Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IStorageSession session, CancellationToken token);

    /// <summary>
    /// Session-free Load for projection storage (#4667 Phase 2). Opens a fresh
    /// connection from the database, executes the load SQL, and returns the
    /// deserialized document. Does not touch any session-shared state — no
    /// version/revision tracker writes, no ItemMap updates, no
    /// <c>MarkAsDocumentLoaded</c>, no <c>ChangeTrackers</c> writes. Safe to call
    /// from parallel async-daemon slice handlers that share an
    /// <see cref="IMartenSession"/>.
    /// </summary>
    Task<T?> LoadProjectedAsync(TId id, IMartenDatabase database, string tenantId, CancellationToken token);

    /// <summary>
    /// Session-free LoadMany for projection storage (#4667 Phase 2). See
    /// <see cref="LoadProjectedAsync"/>.
    /// </summary>
    Task<IReadOnlyList<T>> LoadManyProjectedAsync(TId[] ids, IMartenDatabase database, string tenantId, CancellationToken token);


    TId AssignIdentity(T document, string tenantId, IStorageDatabase database);
    ISqlFragment ByIdFilter(TId id);
    IDeletion HardDeleteForId(TId id, string tenantId);
    DbCommand BuildLoadCommand(TId id, string tenantId);
    DbCommand BuildLoadManyCommand(TId[] ids, string tenantId);
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
