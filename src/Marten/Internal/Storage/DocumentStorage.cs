using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal.CodeGeneration;
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
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Storage;
using Marten.Storage.Metadata;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Internal.Storage;


[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal interface IHaveMetadataColumns
{
    MetadataColumn[] MetadataColumns();
}

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
public abstract class DocumentStorage<T, TId>: IDocumentStorage<T, TId>, IHaveMetadataColumns where T : notnull where TId : notnull
{
    protected readonly string _loadArraySql;
    protected readonly string _loaderSql;
    private readonly string _selectClause;
    private readonly string[] _selectFields;
    private ISqlFragment? _defaultWhere;
    // #4828: computed once in the ctor from the mapping's schema table instead of
    // lazily re-reading the mapping, so no DocumentMapping is retained on the base.
    private readonly MetadataColumn[] _metadataColumns;
    protected Action<T, TId> _setter;
    protected Action<T, string> _setFromString = (_, _) => throw new NotSupportedException();
    protected Action<T, Guid> _setFromGuid = (_, _) => throw new NotSupportedException();

    // #4828: the DeleteStyle drives the soft-delete WHERE filters + the delete
    // fragment. Stored so the base no longer reads DocumentMapping post-construction.
    private readonly DeleteStyle _deleteStyle;

    // #4828: the ADO/SQL-dialect strategy — the movable base builds load commands / id filters /
    // interprets error codes through it, with no direct Npgsql/Postgres reference. Supplied by the
    // concrete (closed-shape) storages off their descriptor; see PostgresStorageDialect.
    protected abstract IStorageDialect Dialect { get; }

    public DocumentStorage(StorageStyle storageStyle, DocumentMapping document)
    {
        // #4828: read everything the base needs off the mapping here, in the ctor,
        // and store it in fields — so no DocumentMapping is retained. The closed-shape
        // storages (the only concrete storages) never read the mapping themselves; this
        // makes the base a self-contained, DocumentMapping-free hierarchy.
        TableName = document.TableName;
        TenancyStyle = document.TenancyStyle;
        DocumentType = document.DocumentType;
        QueryMembers = document.QueryMembers;
        _deleteStyle = document.DeleteStyle;
        _metadataColumns = document.Schema.Table.Columns.OfType<MetadataColumn>().ToArray();

        determineDefaultWhereFragment();

        var table = document.Schema.Table;

        DuplicatedFields = document.DuplicatedFields;

        _selectFields = table.SelectColumns(storageStyle).Select(x => $"d.{x.Name}").ToArray();
        var fieldSelector = _selectFields.Join(", ");
        _selectClause = $"select {fieldSelector} from {document.TableName.QualifiedName} as d";

        _loaderSql =
            $"select {fieldSelector} from {document.TableName.QualifiedName} as d where id = $1";

        _loadArraySql =
            $"select {fieldSelector} from {document.TableName.QualifiedName} as d where id = ANY($1)";

        if (TenancyStyle == TenancyStyle.Conjoined)
        {
            _loaderSql += $" and d.{TenantIdColumn.Name} = $2";
            _loadArraySql += $" and d.{TenantIdColumn.Name} = $2";
        }

        UseOptimisticConcurrency = document.UseOptimisticConcurrency;
        UseNumericRevisions = document.UseNumericRevisions;

        _setter = LambdaBuilder.Setter<T, TId>(document.IdMember)!;
        if (typeof(TId) == typeof(Guid))
        {
            _setFromGuid = _setter.As<Action<T, Guid>>();
        }
        else if (typeof(TId) == typeof(string))
        {
            _setFromString = _setter.As<Action<T, string>>();
        }
        else if (document.IdStrategy is ValueTypeIdGeneration valueType)
        {
            if (valueType.SimpleType == typeof(Guid))
            {
                var converter = valueType.CreateWrapper<TId, Guid>();
                _setFromGuid = (doc, guid) => _setter(doc, converter(guid));
            }
            else if (valueType.SimpleType == typeof(string))
            {
                var converter = valueType.CreateWrapper<TId, string>();
                _setFromString = (doc, s) => _setter(doc, converter(s));
            }
        }

        DeleteFragment = _deleteStyle == DeleteStyle.Remove
            ? new HardDelete(this)
            : new SoftDelete(this);

        HardDeleteFragment = new HardDelete(this);
    }

    object IDocumentStorage.RawIdentityValue(object id)
    {
        return RawIdentityValue((TId)id);
    }

    public bool UseNumericRevisions { get;  }

    // TODO -- convert to a method in V8
    // this has to be a new instance every time because of how it gets the FromObject
    // renamed in Include() batches
    public ISelectClause SelectClauseWithDuplicatedFields
    {
        get
        {
            if (DuplicatedFields.Any())
            {
                var duplicatedFields = DuplicatedFields.Select(x => "d." + x.ColumnName).Where(x => !_selectFields.Contains(x));
                var allFields = _selectFields.Concat(duplicatedFields).ToArray();
                return new DuplicatedFieldSelectClause(TableName.QualifiedName, $"select {allFields.Join(", ")} from {TableName.QualifiedName} as d",
                    allFields, typeof(T), this);
            }
            else
            {
                return this;
            }
        }
    }

    MetadataColumn[] IHaveMetadataColumns.MetadataColumns()
    {
        return _metadataColumns;
    }

    public IQueryableMemberCollection QueryMembers { get; }

    public async Task TruncateDocumentStorageAsync(IMartenDatabase database, CancellationToken ct = default)
    {
        var sql = $"truncate {TableName.QualifiedName} cascade";
        try
        {
            await database.RunSqlAsync(sql, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (Dialect.IsUndefinedTable(e))
        {
            // the table doesn't exist yet — nothing to truncate
        }
    }

    public void SetIdentity(T document, TId identity)
    {
        _setter?.Invoke(document, identity);
    }

    public void SetIdentityFromString(T document, string identityString)
    {
        _setFromString(document, identityString);
    }

    public void SetIdentityFromGuid(T document, Guid identityGuid)
    {
        _setFromGuid(document, identityGuid);
    }


    public TenancyStyle TenancyStyle { get; }

    public Type DocumentType { get; }

    public IReadOnlyList<IDuplicatedField> DuplicatedFields { get; }

    public ISqlFragment ByIdFilter(TId id)
    {
        // IDocumentStorage.ByIdFilter keeps its Weasel.Postgresql return type (public); the dialect's
        // neutral Weasel.Core fragment is always a Marten (Postgres) ByIdFilter, so the cast is safe.
        return (ISqlFragment)Dialect.ByIdFilter(RawIdentityValue(id));
    }

    public IDeletion HardDeleteForId(TId id, string tenant)
    {
        if (TenancyStyle == TenancyStyle.Conjoined)
        {
            return new Deletion(this, HardDeleteFragment, CompoundWhereFragment.And(new SpecificTenantFilter(tenant), ByIdFilter(id)))
            {
                Id = id
            };
        }

        return new Deletion(this, HardDeleteFragment, ByIdFilter(id)) { Id = id };
    }

    public void EjectById(IStorageSession session, object id)
    {
        var typedId = (TId)id;

        if (session.ItemMap.TryGetValue(typeof(T), out var dict))
        {
            if (dict is Dictionary<TId, T> d)
            {
                d.Remove(typedId);
            }
        }
    }

    public void RemoveDirtyTracker(IStorageSession session, object id)
    {
        session.ChangeTrackers.RemoveAll(x =>
        {
            if (x.Document is T doc)
            {
                return Identity(doc).Equals(id);
            }

            return false;
        });
    }

    public IDeletion HardDeleteForDocument(T document, string tenantId)
    {
        var id = Identity(document);

        var deletion = HardDeleteForId(id, tenantId);
        deletion.Document = document;

        return deletion;
    }

    public bool UseOptimisticConcurrency { get; }

    object IDocumentStorage<T>.IdentityFor(T document)
    {
        return Identity(document);
    }

    public Type SelectedType => typeof(T);

    public Type SourceType => typeof(T);


    public abstract TId AssignIdentity(T document, string tenantId, IStorageDatabase database);

    public DbObjectName TableName { get; }

    string ISelectClause.FromObject => TableName.QualifiedName;

    public Type IdType => typeof(TId);

    public Guid? VersionFor(T document, IStorageSession session)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return session.Versions.VersionFor<T, TId>(Identity(document));
    }

    public abstract void Store(IStorageSession session, T document);
    public abstract void Store(IStorageSession session, T document, Guid? version);
    public abstract void Store(IStorageSession session, T document, long revision);
    public abstract void Eject(IStorageSession session, T document);
    public abstract IStorageOperation Update(T document, IStorageSession session, string tenant);
    public abstract IStorageOperation Insert(T document, IStorageSession session, string tenant);
    public abstract IStorageOperation Upsert(T document, IStorageSession session, string tenant);

    public abstract IStorageOperation Overwrite(T document, IStorageSession session, string tenant);

    /// <inheritdoc />
    public abstract IStorageOperation OverwriteProjected(T document, string tenant);

    /// <inheritdoc />
    public abstract IStorageOperation UpsertProjected(T document, string tenant);

    /// <inheritdoc />
    public abstract IStorageOperation InsertProjected(T document, string tenant);

    /// <inheritdoc />
    public abstract IStorageOperation UpdateProjected(T document, string tenant);

    public IDeletion DeleteForDocument(T document, string tenant)
    {
        var id = Identity(document);

        var deletion = DeleteForId(id, tenant);
        deletion.Document = document;

        return deletion;
    }

    public IDeletion DeleteForId(TId id, string tenant)
    {
        if (TenancyStyle == TenancyStyle.Conjoined)
        {
            return new Deletion(this, DeleteFragment, CompoundWhereFragment.And(new SpecificTenantFilter(tenant), ByIdFilter(id)))
            {
                Id = id
            };
        }

        return new Deletion(this, DeleteFragment, ByIdFilter(id))
        {
            Id = id
        };
    }

    public IOperationFragment DeleteFragment { get; }

    public IOperationFragment HardDeleteFragment { get; }

    public ISqlFragment FilterDocuments(ISqlFragment query, IStorageSession session)
    {
        var extras = extraFilters(query, session).ToList();

        if (extras.Count > 0)
        {
            extras.Add(query);
            return CompoundWhereFragment.And(extras);
        }

        return query;
    }

    public ISqlFragment? DefaultWhereFragment()
    {
        return _defaultWhere;
    }

    public abstract Task<T?> LoadAsync(TId id, IStorageSession session, CancellationToken token);

    public abstract Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IStorageSession session, CancellationToken token);

    /// <inheritdoc />
    /// <remarks>
    /// Default impl throws — closed-shape storages (the projection-eligible
    /// path) override with a fresh-connection read that bypasses session-shared
    /// trackers. Non-closed-shape storages aren't reachable via the projection
    /// write path and don't need this overload.
    /// </remarks>
    public virtual Task<T?> LoadProjectedAsync(TId id, IMartenDatabase database, string tenantId, CancellationToken token)
        => throw new NotSupportedException(
            $"{GetType().Name} doesn't implement LoadProjectedAsync. Closed-shape storage variants provide this for the async-daemon projection-safe read path (#4667 Phase 2).");

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<T>> LoadManyProjectedAsync(TId[] ids, IMartenDatabase database, string tenantId, CancellationToken token)
        => throw new NotSupportedException(
            $"{GetType().Name} doesn't implement LoadManyProjectedAsync. Closed-shape storage variants provide this for the async-daemon projection-safe read path (#4667 Phase 2).");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract TId Identity(T document);


    public void Apply(ICommandBuilder sql)
    {
        sql.Append(_selectClause);
    }

    public string[] SelectFields()
    {
        return _selectFields;
    }

    public abstract ISelector BuildSelector(IStorageSession session);

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

    public virtual object RawIdentityValue(TId id) => id;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DbCommand BuildLoadCommand(TId id, string tenant)
        // #4828: the (Postgres) dialect materializes the command; cast back to keep the public
        // IDocumentStorage.BuildLoadCommand signature (NpgsqlCommand) until it is widened to DbCommand.
        => Dialect.BuildLoadCommand(_loaderSql,
            RawIdentityValue(id),
            TenancyStyle == TenancyStyle.Conjoined ? tenant : null);

    // #4828: default id-array parameter via the dialect. Closed-shape storages override this to
    // project strong-typed ids to their raw values first (using the descriptor's Identification).
    public virtual DbParameter BuildManyIdParameter(TId[] ids)
        => Dialect.CreateIdArrayParameter(ids, typeof(TId));

    public DbCommand BuildLoadManyCommand(TId[] ids, string tenant)
        => Dialect.BuildLoadManyCommand(_loadArraySql,
            BuildManyIdParameter(ids),
            TenancyStyle == TenancyStyle.Conjoined ? tenant : null);

    private void determineDefaultWhereFragment()
    {
        var defaults = defaultFilters().ToArray();
        _defaultWhere = defaults.Length switch
        {
            0 => null,
            1 => defaults[0],
            _ => CompoundWhereFragment.And(defaults)
        };
    }

    private IEnumerable<ISqlFragment> extraFilters(ISqlFragment query, IStorageSession session)
    {
        if (_deleteStyle == DeleteStyle.SoftDelete && !query.ContainsAny<ISoftDeletedFilter>())
        {
            yield return ExcludeSoftDeletedFilter.Instance;
        }

        if (TenancyStyle == TenancyStyle.Conjoined && !query.SpecifiesTenant())
        {
            yield return new DefaultTenantFilter(session.TenantId);
        }
    }

    private IEnumerable<ISqlFragment> defaultFilters()
    {
        if (_deleteStyle == DeleteStyle.SoftDelete)
        {
            yield return ExcludeSoftDeletedFilter.Instance;
        }

        if (TenancyStyle == TenancyStyle.Conjoined)
        {
            yield return CurrentTenantFilter.Instance;
        }
    }

    protected async Task<T?> loadAsync(TId id, IStorageSession session, CancellationToken token)
    {
        var command = BuildLoadCommand(id, session.TenantId);
        var selector = (ISelector<T>)BuildSelector(session);

        // #4828: read through the agnostic IStorageSession.ExecuteReaderAsync(DbCommand)
        // execution seam (#4810) instead of downcasting to QuerySession.LoadOneAsync —
        // mirrors the LoadManyAsync overrides. Behavior-identical to
        // QuerySession.LoadOneAsync (execute → read one → resolve → close).
        await using var reader = await session.ExecuteReaderAsync(command, token).ConfigureAwait(false);
        try
        {
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return default;
            }

            return await selector.ResolveAsync(reader, token).ConfigureAwait(false);
        }
        finally
        {
            await reader.CloseAsync().ConfigureAwait(false);
        }
    }
}


[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal class DuplicatedFieldSelectClause: ISelectClause, IModifyableFromObject
{
    private string[] _selectFields;

    internal void EnsureColumn(string columnLocator)
    {
        if (!_selectFields.Contains(columnLocator))
        {
            _selectFields = [.._selectFields, columnLocator];
        }
    }
    private readonly IDocumentStorage _parent;

    public DuplicatedFieldSelectClause(string fromObject, string selector, string[] selectFields, Type selectedType,
        IDocumentStorage parent)
    {
        FromObject = fromObject;
        _selectFields = selectFields;
        _parent = parent;
        SelectedType = selectedType;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("select ");
        builder.Append(_selectFields.Join(", "));
        builder.Append(" from ");
        builder.Append(FromObject);
        builder.Append(" as d");
    }

    public string FromObject
    {
        get;
        set;
    }


    public Type SelectedType { get; }
    public string[] SelectFields()
    {
        return _selectFields;
    }

    public ISelector BuildSelector(IStorageSession session)
    {
        return _parent.BuildSelector(session);
    }

    public IQueryHandler<T> BuildHandler<T>(IStorageSession session, ISqlFragment topStatement, ISqlFragment currentStatement) where T : notnull
    {
        return _parent.BuildHandler<T>(session, topStatement, currentStatement);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return typeof(StatsSelectClause<>).CloseAndBuildAs<ISelectClause>(this, statistics, SelectedType);
    }
}
