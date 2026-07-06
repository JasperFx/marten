using System;
using System.Data.Common;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Storage.Metadata;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Internal.Storage;

internal class SubClassDocumentStorage<T, TRoot, TId>: IDocumentStorage<T, TId>, ILinqDocumentStorage, IHaveMetadataColumns
    where T : notnull, TRoot where TId : notnull where TRoot : notnull
{
    private readonly ISqlFragment? _defaultWhere;
    private readonly string[] _fields;
    private readonly SubClassMapping _mapping;
    private readonly IDocumentStorage<TRoot, TId> _parent;

    public SubClassDocumentStorage(IDocumentStorage<TRoot, TId> parent, SubClassMapping mapping)
    {
        _parent = parent;
        _mapping = mapping;

        FromObject = _mapping.TableName.QualifiedName;

        _defaultWhere = determineWhereFragment();
        _fields = _parent.SelectFields();
    }

    public IQueryableMemberCollection QueryMembers => _mapping.QueryMembers;
    public ISelectClause SelectClauseWithDuplicatedFields => ((ILinqDocumentStorage)_parent).SelectClauseWithDuplicatedFields;
    public bool UseNumericRevisions { get; } = false;
    public object RawIdentityValue(object id)
    {
        return _parent.RawIdentityValue(id);
    }

    public Task TruncateDocumentStorageAsync(IStorageDatabase database, CancellationToken ct = default)
    {
        return database.RunSqlAsync(
            $"delete from {_parent.TableName.QualifiedName} where {SchemaConstants.DocumentTypeColumn} = '{_mapping.Alias}'",
            ct: ct);
    }

    public TenancyStyle TenancyStyle => _parent.TenancyStyle;

    object IDocumentStorage<T>.IdentityFor(T document)
    {
        return _parent.Identity(document);
    }

    public string FromObject { get; }
    public Type SelectedType => typeof(T);

    public void Apply(ICommandBuilder sql)
    {
        _parent.Apply(sql);
    }

    public string[] SelectFields()
    {
        return _fields;
    }

    public ISelector BuildSelector(IStorageSession session)
    {
        var inner = _parent.BuildSelector(session);
        return new CastingSelector<T, TRoot>((ISelector<TRoot>)inner);
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(IStorageSession session, ISqlFragment statement,
        ISqlFragment currentStatement) where TResult : notnull
    {
        var selector = (ISelector<T>)BuildSelector(session);

        return LinqQueryParser.BuildHandler<T, TResult>(selector, statement);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return new StatsSelectClause<T>(this, statistics);
    }

    public Type SourceType => typeof(TRoot);

    public Weasel.Core.SqlGeneration.ISqlFragment FilterDocuments(Weasel.Core.SqlGeneration.ISqlFragment query, IStorageSession session)
    {
        var pgQuery = (ISqlFragment)query;
        var extras = extraFilters(pgQuery, session).ToArray();

        return pgQuery.CombineAnd(extras);
    }

    public Weasel.Core.SqlGeneration.ISqlFragment? DefaultWhereFragment()
    {
        return _defaultWhere;
    }


    public bool UseOptimisticConcurrency => _parent.UseOptimisticConcurrency;
    public IOperationFragment DeleteFragment => _parent.DeleteFragment;
    public IOperationFragment HardDeleteFragment { get; }
    public IReadOnlyList<IDuplicatedField> DuplicatedFields => _parent.DuplicatedFields;
    public DbObjectName TableName => _parent.TableName;
    public Type DocumentType => typeof(T);

    public Type IdType => typeof(TId);

    public Guid? VersionFor(T document, IStorageSession session)
    {
        return _parent.VersionFor(document, session);
    }

    public void Store(IStorageSession session, T document)
    {
        _parent.Store(session, document);
    }

    public void Store(IStorageSession session, T document, Guid? version)
    {
        _parent.Store(session, document, version);
    }

    public void Store(IStorageSession session, T document, long revision)
    {
        _parent.Store(session, document, revision);
    }

    public void Eject(IStorageSession session, T document)
    {
        _parent.Eject(session, document);
    }

    public Weasel.Storage.IStorageOperation Update(T document, IStorageSession session, string tenant)
    {
        return _parent.Update(document, session, tenant);
    }

    public Weasel.Storage.IStorageOperation Insert(T document, IStorageSession session, string tenant)
    {
        return _parent.Insert(document, session, tenant);
    }

    public Weasel.Storage.IStorageOperation Upsert(T document, IStorageSession session, string tenant)
    {
        return _parent.Upsert(document, session, tenant);
    }

    public Weasel.Storage.IStorageOperation Overwrite(T document, IStorageSession session, string tenant)
    {
        return _parent.Overwrite(document, session, tenant);
    }

    public Weasel.Storage.IStorageOperation OverwriteProjected(T document, string tenant)
    {
        return _parent.OverwriteProjected(document, tenant);
    }

    // #4667 — delegate the new projection write entry points to the parent
    // hierarchy storage just like Overwrite/OverwriteProjected do.
    public Weasel.Storage.IStorageOperation UpsertProjected(T document, string tenant)
    {
        return _parent.UpsertProjected(document, tenant);
    }

    public Weasel.Storage.IStorageOperation InsertProjected(T document, string tenant)
    {
        return _parent.InsertProjected(document, tenant);
    }

    public Weasel.Storage.IStorageOperation UpdateProjected(T document, string tenant)
    {
        return _parent.UpdateProjected(document, tenant);
    }

    public IDeletion DeleteForDocument(T document, string tenant)
    {
        return _parent.DeleteForDocument(document, tenant);
    }

    public void SetIdentity(T document, TId identity)
    {
        _parent.SetIdentity(document, identity);
    }

    public IDeletion DeleteForId(TId id, string tenant)
    {
        return _parent.DeleteForId(id, tenant);
    }

    public async Task<T?> LoadAsync(TId id, IStorageSession session, CancellationToken token)
    {
        var doc = await _parent.LoadAsync(id, session, token).ConfigureAwait(false);

        if (doc is T x)
        {
            return x;
        }

        return default;
    }

    public async Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IStorageSession session, CancellationToken token)
    {
        return (await _parent.LoadManyAsync(ids, session, token).ConfigureAwait(false)).OfType<T>().ToList();
    }

    // #4667 Phase 2 — delegate projection loads to the parent hierarchy storage
    // and downcast to the subclass like the session-aware path above.
    public async Task<T?> LoadProjectedAsync(TId id, IStorageDatabase database, string tenantId, CancellationToken token)
    {
        var doc = await _parent.LoadProjectedAsync(id, database, tenantId, token).ConfigureAwait(false);
        return doc is T x ? x : default;
    }

    public async Task<IReadOnlyList<T>> LoadManyProjectedAsync(TId[] ids, IStorageDatabase database, string tenantId, CancellationToken token)
    {
        return (await _parent.LoadManyProjectedAsync(ids, database, tenantId, token).ConfigureAwait(false)).OfType<T>().ToList();
    }

    public TId AssignIdentity(T document, string tenantId, IStorageDatabase database)
    {
        return _parent.AssignIdentity(document, tenantId, database);
    }

    public TId Identity(T document)
    {
        return _parent.Identity(document);
    }

    public Weasel.Core.SqlGeneration.ISqlFragment ByIdFilter(TId id)
    {
        return _parent.ByIdFilter(id);
    }

    public IDeletion HardDeleteForId(TId id, string tenant)
    {
        return _parent.HardDeleteForId(id, tenant);
    }

    public DbCommand BuildLoadCommand(TId id, string tenant)
    {
        return _parent.BuildLoadCommand(id, tenant);
    }

    public DbCommand BuildLoadManyCommand(TId[] ids, string tenant)
    {
        return _parent.BuildLoadManyCommand(ids, tenant);
    }

    public object RawIdentityValue(TId id)
    {
        return _parent.RawIdentityValue(id);
    }

    public void EjectById(IStorageSession session, object id)
    {
        _parent.EjectById(session, id);
    }

    public void RemoveDirtyTracker(IStorageSession session, object id)
    {
        _parent.RemoveDirtyTracker(session, id);
    }

    public IDeletion HardDeleteForDocument(T document, string tenantId)
    {
        return _parent.HardDeleteForDocument(document, tenantId);
    }

    public void SetIdentityFromString(T document, string identityString)
    {
        _parent.SetIdentityFromString(document, identityString);
    }

    public void SetIdentityFromGuid(T document, Guid identityGuid)
    {
        _parent.SetIdentityFromGuid(document, identityGuid);
    }

    private IEnumerable<ISqlFragment> extraFilters(ISqlFragment query, IStorageSession session)
    {
        yield return toBasicWhere();

        if (_mapping.DeleteStyle == DeleteStyle.SoftDelete && !query.ContainsAny<ISoftDeletedFilter>())
        {
            yield return ExcludeSoftDeletedFilter.Instance;
        }

        if (_mapping.Parent.TenancyStyle == TenancyStyle.Conjoined && !query.SpecifiesTenant())
        {
            yield return new SpecificTenantFilter(session.TenantId);
        }
    }

    private IEnumerable<ISqlFragment> defaultFilters()
    {
        yield return toBasicWhere();

        if (_mapping.Parent.TenancyStyle == TenancyStyle.Conjoined)
        {
            yield return CurrentTenantFilter.Instance;
        }

        if (_mapping.DeleteStyle == DeleteStyle.SoftDelete)
        {
            yield return ExcludeSoftDeletedFilter.Instance;
        }
    }

    public ISqlFragment? determineWhereFragment()
    {
        var defaults = defaultFilters().ToArray();
        return defaults.Length switch
        {
            0 => null,
            1 => defaults[0],
            _ => CompoundWhereFragment.And(defaults)
        };
    }

    private WhereFragment toBasicWhere()
    {
        var aliasValues = _mapping.Aliases.Select(a => $"d.{SchemaConstants.DocumentTypeColumn} = '{a}'").ToArray()
            .Join(" or ");

        var sql = _mapping.Alias.Length > 1 ? $"({aliasValues})" : aliasValues;
        return new WhereFragment(sql);
    }

    public MetadataColumn[] MetadataColumns()
    {
        return _parent.As<IHaveMetadataColumns>().MetadataColumns();
    }
}
