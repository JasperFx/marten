using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Internal.Operations;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using Remotion.Linq;
using Weasel.Core;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Internal.Storage
{
    internal class SubClassDocumentStorage<T, TRoot, TId>: IDocumentStorage<T, TId>
        where T : TRoot
    {
        private readonly IDocumentStorage<TRoot, TId> _parent;
        private readonly SubClassMapping _mapping;
        private readonly ISqlFragment _defaultWhere;
        private readonly string[] _fields;

        public SubClassDocumentStorage(IDocumentStorage<TRoot, TId> parent, SubClassMapping mapping)
        {
            _parent = parent;
            _mapping = mapping;

            FromObject = _mapping.TableName.QualifiedName;

            _defaultWhere = determineWhereFragment();
            _fields = _parent.SelectFields();
        }

        public void TruncateDocumentStorage(ITenant tenant)
        {
            tenant.RunSql(
                $"delete from {_parent.TableName.QualifiedName} where {SchemaConstants.DocumentTypeColumn} = '{_mapping.Alias}'");
        }

        public Task TruncateDocumentStorageAsync(ITenant tenant)
        {
            return tenant.RunSqlAsync(
                $"delete from {_parent.TableName.QualifiedName} where {SchemaConstants.DocumentTypeColumn} = '{_mapping.Alias}'");
        }

        public TenancyStyle TenancyStyle => _parent.TenancyStyle;

        object IDocumentStorage<T>.IdentityFor(T document)
        {
            return _parent.Identity(document);
        }

        public string FromObject { get; }
        public Type SelectedType => typeof(T);

        public void WriteSelectClause(CommandBuilder sql)
        {
            _parent.WriteSelectClause(sql);
        }

        public string[] SelectFields()
        {
            return _fields;
        }

        public ISelector BuildSelector(IMartenSession session)
        {
            var inner = _parent.BuildSelector(session);
            return new CastingSelector<T, TRoot>((ISelector<TRoot>) inner);
        }

        public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, Statement statement,
            Statement currentStatement)
        {
            var selector = (ISelector<T>)BuildSelector(session);

            return LinqHandlerBuilder.BuildHandler<T, TResult>(selector, statement);
        }

        public ISelectClause UseStatistics(QueryStatistics statistics)
        {
            return new StatsSelectClause<T>(this, statistics);
        }

        public Type SourceType => typeof(TRoot);
        public IFieldMapping Fields => _mapping.Parent;

        public ISqlFragment FilterDocuments(QueryModel model, ISqlFragment query)
        {
            var extras = extraFilters(query).ToArray();

            return query.CombineAnd(extras);
        }

        private IEnumerable<ISqlFragment> extraFilters(ISqlFragment query)
        {
            yield return toBasicWhere();

            if (_mapping.DeleteStyle == DeleteStyle.SoftDelete && !query.Contains(SchemaConstants.DeletedColumn))
                yield return ExcludeSoftDeletedFilter.Instance;

            if (_mapping.Parent.TenancyStyle == TenancyStyle.Conjoined && !query.SpecifiesTenant())
                yield return CurrentTenantFilter.Instance;
        }

        private IEnumerable<ISqlFragment> defaultFilters()
        {
            yield return toBasicWhere();

            if (_mapping.Parent.TenancyStyle == TenancyStyle.Conjoined) yield return CurrentTenantFilter.Instance;

            if (_mapping.DeleteStyle == DeleteStyle.SoftDelete) yield return ExcludeSoftDeletedFilter.Instance;
        }

        public ISqlFragment DefaultWhereFragment()
        {
            return _defaultWhere;
        }

        public ISqlFragment determineWhereFragment()
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


        public bool UseOptimisticConcurrency => _parent.UseOptimisticConcurrency;
        public IOperationFragment DeleteFragment => _parent.DeleteFragment;
        public IOperationFragment HardDeleteFragment { get; }
        public DuplicatedField[] DuplicatedFields => _parent.DuplicatedFields;
        public DbObjectName TableName => _parent.TableName;
        public Type DocumentType => typeof(T);

        public Type IdType => typeof(TId);
        public Guid? VersionFor(T document, IMartenSession session)
        {
            return _parent.VersionFor(document, session);
        }

        public void Store(IMartenSession session, T document)
        {
            _parent.Store(session, document);
        }

        public void Store(IMartenSession session, T document, Guid? version)
        {
            _parent.Store(session, document, version);
        }

        public void Eject(IMartenSession session, T document)
        {
            _parent.Eject(session, document);
        }

        public IStorageOperation Update(T document, IMartenSession session, ITenant tenant)
        {
            return _parent.Update(document, session, tenant);
        }

        public IStorageOperation Insert(T document, IMartenSession session, ITenant tenant)
        {
            return _parent.Insert(document, session, tenant);
        }

        public IStorageOperation Upsert(T document, IMartenSession session, ITenant tenant)
        {
            return _parent.Upsert(document, session, tenant);
        }

        public IStorageOperation Overwrite(T document, IMartenSession session, ITenant tenant)
        {
            return _parent.Overwrite(document, session, tenant);
        }

        public IDeletion DeleteForDocument(T document, ITenant tenant)
        {
            return _parent.DeleteForDocument(document, tenant);
        }

        public void SetIdentity(T document, TId identity)
        {
            _parent.SetIdentity(document, identity);
        }

        public IDeletion DeleteForId(TId id, ITenant tenant)
        {
            return _parent.DeleteForId(id, tenant);
        }

        public T Load(TId id, IMartenSession session)
        {
            var doc = _parent.Load(id, session);

            if (doc is T x) return x;

            return default;
        }

        public async Task<T> LoadAsync(TId id, IMartenSession session, CancellationToken token)
        {
            var doc = await _parent.LoadAsync(id, session, token);

            if (doc is T x) return x;

            return default;
        }

        public IReadOnlyList<T> LoadMany(TId[] ids, IMartenSession session)
        {
            return _parent.LoadMany(ids, session).OfType<T>().ToList();
        }

        public async Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IMartenSession session, CancellationToken token)
        {
            return (await _parent.LoadManyAsync(ids, session, token)).OfType<T>().ToList();
        }

        public TId AssignIdentity(T document, ITenant tenant)
        {
            return _parent.AssignIdentity(document, tenant);
        }

        public TId Identity(T document)
        {
            return _parent.Identity(document);
        }

        public ISqlFragment ByIdFilter(TId id)
        {
            return _parent.ByIdFilter(id);
        }

        public IDeletion HardDeleteForId(TId id, ITenant tenant)
        {
            return _parent.HardDeleteForId(id, tenant);
        }

        public NpgsqlCommand BuildLoadCommand(TId id, ITenant tenant)
        {
            return _parent.BuildLoadCommand(id, tenant);
        }

        public NpgsqlCommand BuildLoadManyCommand(TId[] ids, ITenant tenant)
        {
            return _parent.BuildLoadManyCommand(ids, tenant);
        }

        public void EjectById(IMartenSession session, object id)
        {
            _parent.EjectById(session, id);
        }

        public void RemoveDirtyTracker(IMartenSession session, object id)
        {
            _parent.RemoveDirtyTracker(session, id);
        }

        public IDeletion HardDeleteForDocument(T document, ITenant tenant)
        {
            return _parent.HardDeleteForDocument(document, tenant);
        }
    }
}
