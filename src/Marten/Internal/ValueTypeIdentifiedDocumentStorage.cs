using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events.Aggregation;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Internal;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
internal class ValueTypeIdentifiedIdentitySetter<TDoc, TSimple, TValueType>: IIdentitySetter<TDoc, TSimple>
{
    private readonly Func<TSimple, TValueType> _converter;
    private readonly Func<TValueType, TSimple> _unwrapper;

    public ValueTypeIdentifiedIdentitySetter(ValueTypeInfo valueTypeInfo, IDocumentStorage<TDoc, TValueType> inner)
    {
        Inner = inner;

        _converter = valueTypeInfo.CreateWrapper<TValueType, TSimple>();
        _unwrapper = valueTypeInfo.UnWrapper<TValueType, TSimple>();
    }

    public void SetIdentity(TDoc document, TSimple identity)
        => Inner.SetIdentity(document, _converter(identity));

    public TSimple Identity(TDoc document)
    {
        return _unwrapper(Inner.Identity(document));
    }

    public IIdentitySetter<TDoc, TValueType> Inner { get; }
}

internal interface IValueTypeStorage<TDoc, TValueType>
{
    IQueryHandler<IReadOnlyList<TDoc>> BuildLoadManyHandler(TValueType[] keys);
}

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
internal class ValueTypeIdentifiedDocumentStorage<TDoc, TSimple, TValueType>: IDocumentStorage<TDoc, TSimple>,  IValueTypeStorage<TDoc, TValueType> where TDoc : notnull where TSimple : notnull where TValueType : notnull
{
    private readonly Func<TSimple, TValueType> _converter;
    private readonly Func<TValueType,TSimple> _unwrapper;

    public ValueTypeIdentifiedDocumentStorage(ValueTypeInfo valueTypeInfo, IDocumentStorage<TDoc, TValueType> inner)
    {
        Inner = inner;

        _converter = valueTypeInfo.CreateWrapper<TValueType, TSimple>();
        _unwrapper = valueTypeInfo.UnWrapper<TValueType, TSimple>();
    }

    public IQueryHandler<IReadOnlyList<TDoc>> BuildLoadManyHandler(TValueType[] keys)
    {
        var ids = keys.Select(x => _unwrapper(x)).ToArray();
        return new LoadByIdArrayHandler<TDoc, TSimple>(Inner, ids);
    }

    public IDocumentStorage<TDoc, TValueType> Inner { get; }

    public void Apply(ICommandBuilder builder) => Inner.Apply(builder);

    public string FromObject => Inner.FromObject;
    public Type SelectedType => Inner.SelectedType;
    public string[] SelectFields() => Inner.SelectFields();

    public ISelector BuildSelector(IStorageSession session) => Inner.BuildSelector(session);

    public IQueryHandler<T> BuildHandler<T>(IStorageSession session, ISqlFragment topStatement,
        ISqlFragment currentStatement) where T : notnull => Inner.BuildHandler<T>(session, topStatement, currentStatement);

    public ISelectClause UseStatistics(QueryStatistics statistics)
        => Inner.UseStatistics(statistics);

    public Type SourceType => Inner.SourceType;
    public Type IdType => Inner.IdType;
    public bool UseOptimisticConcurrency => Inner.UseOptimisticConcurrency;
    public IOperationFragment DeleteFragment => Inner.DeleteFragment;
    public IOperationFragment HardDeleteFragment => Inner.HardDeleteFragment;
    public IReadOnlyList<DuplicatedField> DuplicatedFields => Inner.DuplicatedFields;
    public DbObjectName TableName => Inner.TableName;
    public Type DocumentType => Inner.DocumentType;
    public TenancyStyle TenancyStyle => Inner.TenancyStyle;

    public Task TruncateDocumentStorageAsync(IMartenDatabase database, CancellationToken ct = default)
        => Inner.TruncateDocumentStorageAsync(database, ct);

    public ISqlFragment FilterDocuments(ISqlFragment query, IStorageSession session)
        => Inner.FilterDocuments(query, session);

    public ISqlFragment DefaultWhereFragment()
        => Inner.DefaultWhereFragment();

    public IQueryableMemberCollection QueryMembers => Inner.QueryMembers;
    public ISelectClause SelectClauseWithDuplicatedFields => Inner.SelectClauseWithDuplicatedFields;
    public bool UseNumericRevisions => Inner.UseNumericRevisions;
    public object RawIdentityValue(object id) => Inner.RawIdentityValue(id);

    public object IdentityFor(TDoc document) => Inner.IdentityFor(document);

    public Guid? VersionFor(TDoc document, IStorageSession session) => Inner.VersionFor(document, session);

    public void Store(IStorageSession session, TDoc document) => Inner.Store(session, document);

    public void Store(IStorageSession session, TDoc document, Guid? version) => Inner.Store(session, document, version);

    public void Store(IStorageSession session, TDoc document, long revision) => Inner.Store(session, document, revision);

    public void Eject(IStorageSession session, TDoc document) => Inner.Eject(session, document);

    public IStorageOperation Update(TDoc document, IStorageSession session, string tenantId) =>
        Inner.Update(document, session, tenantId);

    public IStorageOperation Insert(TDoc document, IStorageSession session, string tenantId)
        => Inner.Insert(document, session, tenantId);

    public IStorageOperation Upsert(TDoc document, IStorageSession session, string tenantId)
        => Inner.Upsert(document, session, tenantId);

    public IStorageOperation Overwrite(TDoc document, IStorageSession session, string tenantId)
        => Inner.Overwrite(document, session, tenantId);

    public IStorageOperation OverwriteProjected(TDoc document, string tenantId)
        => Inner.OverwriteProjected(document, tenantId);

    // #4667 — delegate the projection write entry points.
    public IStorageOperation UpsertProjected(TDoc document, string tenantId)
        => Inner.UpsertProjected(document, tenantId);

    public IStorageOperation InsertProjected(TDoc document, string tenantId)
        => Inner.InsertProjected(document, tenantId);

    public IStorageOperation UpdateProjected(TDoc document, string tenantId)
        => Inner.UpdateProjected(document, tenantId);

    public IDeletion DeleteForDocument(TDoc document, string tenantId)
        => Inner.DeleteForDocument(document, tenantId);

    public void EjectById(IStorageSession session, object id)
        => Inner.EjectById(session, id);

    public void RemoveDirtyTracker(IStorageSession session, object id)
        => Inner.RemoveDirtyTracker(session, id);

    public IDeletion HardDeleteForDocument(TDoc document, string tenantId)
        => Inner.HardDeleteForDocument(document, tenantId);

    public void SetIdentityFromString(TDoc document, string identityString)
        => Inner.SetIdentityFromString(document, identityString);

    public void SetIdentityFromGuid(TDoc document, Guid identityGuid)
        => Inner.SetIdentityFromGuid(document, identityGuid);

    public void SetIdentity(TDoc document, TSimple identity)
        => Inner.SetIdentity(document, _converter(identity));

    public IDeletion DeleteForId(TSimple id, string tenantId)
        => Inner.DeleteForId(_converter(id), tenantId);

    public Task<TDoc?> LoadAsync(TSimple id, IStorageSession session, CancellationToken token)
        => Inner.LoadAsync(_converter(id), session, token);

    public Task<IReadOnlyList<TDoc>> LoadManyAsync(TSimple[] ids, IStorageSession session, CancellationToken token)
        => Inner.LoadManyAsync(ids.Select(_converter).ToArray(), session, token);

    // #4667 Phase 2 — delegate to inner with the unwrapped id like the session-aware path.
    public Task<TDoc?> LoadProjectedAsync(TSimple id, IMartenDatabase database, string tenantId, CancellationToken token)
        => Inner.LoadProjectedAsync(_converter(id), database, tenantId, token);

    public Task<IReadOnlyList<TDoc>> LoadManyProjectedAsync(TSimple[] ids, IMartenDatabase database, string tenantId, CancellationToken token)
        => Inner.LoadManyProjectedAsync(ids.Select(_converter).ToArray(), database, tenantId, token);

    public TSimple AssignIdentity(TDoc document, string tenantId, IStorageDatabase database)
        => _unwrapper(Inner.AssignIdentity(document, tenantId, database));

    public TSimple Identity(TDoc document) => _unwrapper(Inner.Identity(document));

    public ISqlFragment ByIdFilter(TSimple id) => Inner.ByIdFilter(_converter(id));

    public IDeletion HardDeleteForId(TSimple id, string tenantId)
        => Inner.HardDeleteForId(_converter(id), tenantId);

    public NpgsqlCommand BuildLoadCommand(TSimple id, string tenantId)
        => Inner.BuildLoadCommand(_converter(id), tenantId);

    public NpgsqlCommand BuildLoadManyCommand(TSimple[] ids, string tenantId)
        => Inner.BuildLoadManyCommand(ids.Select(_converter).ToArray(), tenantId);

    public object RawIdentityValue(TSimple id) => id;
}
