using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
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

namespace Marten.Internal;

internal class ValueTypeIdentifiedDocumentStorage<TDoc, TSimple, TValueType>: IDocumentStorage<TDoc, TSimple>
{
    private readonly Func<TSimple, TValueType> _converter;
    private readonly Func<TValueType,TSimple> _unwrapper;

    public ValueTypeIdentifiedDocumentStorage(ValueTypeInfo valueTypeInfo, IDocumentStorage<TDoc, TValueType> inner)
    {
        Inner = inner;

        _converter = valueTypeInfo.CreateWrapper<TValueType, TSimple>();
        _unwrapper = valueTypeInfo.UnWrapper<TValueType, TSimple>();
    }

    public IDocumentStorage<TDoc, TValueType> Inner { get; }

    public void Apply(ICommandBuilder builder) => Inner.Apply(builder);

    public string FromObject => Inner.FromObject;
    public Type SelectedType => Inner.SelectedType;
    public string[] SelectFields() => Inner.SelectFields();

    public ISelector BuildSelector(IMartenSession session) => Inner.BuildSelector(session);

    public IQueryHandler<T> BuildHandler<T>(IMartenSession session, ISqlFragment topStatement,
        ISqlFragment currentStatement)
        => Inner.BuildHandler<T>(session, topStatement, currentStatement);

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

    public ISqlFragment FilterDocuments(ISqlFragment query, IMartenSession session)
        => Inner.FilterDocuments(query, session);

    public ISqlFragment DefaultWhereFragment()
        => Inner.DefaultWhereFragment();

    public IQueryableMemberCollection QueryMembers => Inner.QueryMembers;
    public ISelectClause SelectClauseWithDuplicatedFields => Inner.SelectClauseWithDuplicatedFields;
    public bool UseNumericRevisions => Inner.UseNumericRevisions;
    public object RawIdentityValue(object id) => Inner.RawIdentityValue(id);

    public object IdentityFor(TDoc document) => Inner.IdentityFor(document);

    public Guid? VersionFor(TDoc document, IMartenSession session) => Inner.VersionFor(document, session);

    public void Store(IMartenSession session, TDoc document) => Inner.Store(session, document);

    public void Store(IMartenSession session, TDoc document, Guid? version) => Inner.Store(session, document, version);

    public void Store(IMartenSession session, TDoc document, int revision) => Inner.Store(session, document, revision);

    public void Eject(IMartenSession session, TDoc document) => Inner.Eject(session, document);

    public IStorageOperation Update(TDoc document, IMartenSession session, string tenantId) =>
        Inner.Update(document, session, tenantId);

    public IStorageOperation Insert(TDoc document, IMartenSession session, string tenantId)
        => Inner.Insert(document, session, tenantId);

    public IStorageOperation Upsert(TDoc document, IMartenSession session, string tenantId)
        => Inner.Upsert(document, session, tenantId);

    public IStorageOperation Overwrite(TDoc document, IMartenSession session, string tenantId)
        => Inner.Overwrite(document, session, tenantId);

    public IDeletion DeleteForDocument(TDoc document, string tenantId)
        => Inner.DeleteForDocument(document, tenantId);

    public void EjectById(IMartenSession session, object id)
        => Inner.EjectById(session, id);

    public void RemoveDirtyTracker(IMartenSession session, object id)
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

    public Task<TDoc> LoadAsync(TSimple id, IMartenSession session, CancellationToken token)
        => Inner.LoadAsync(_converter(id), session, token);

    public Task<IReadOnlyList<TDoc>> LoadManyAsync(TSimple[] ids, IMartenSession session, CancellationToken token)
        => Inner.LoadManyAsync(ids.Select(_converter).ToArray(), session, token);

    public TSimple AssignIdentity(TDoc document, string tenantId, IMartenDatabase database)
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
