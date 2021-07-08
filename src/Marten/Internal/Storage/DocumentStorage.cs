using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Internal.CodeGeneration;
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
using NpgsqlTypes;
using Remotion.Linq;
using Weasel.Core;
using Weasel.Postgresql.SqlGeneration;
using LambdaBuilder = Baseline.Expressions.LambdaBuilder;
#nullable enable
namespace Marten.Internal.Storage
{

    public abstract class DocumentStorage<T, TId>: IDocumentStorage<T, TId> where T: notnull where TId: notnull
    {
        private ISqlFragment? _defaultWhere;
        private readonly string _selectClause;

        protected readonly string _loadArraySql;
        protected readonly string _loaderSql;
        protected Action<T, TId> _setter;
        protected readonly DocumentMapping _mapping;
        private readonly NpgsqlDbType _idType;
        private readonly string[] _selectFields;

        public DocumentStorage(StorageStyle storageStyle, DocumentMapping document)
        {
            _mapping = document;

            Fields = document;
            TableName = document.TableName;

            determineDefaultWhereFragment();

            _idType = PostgresqlProvider.Instance.ToParameterType(typeof(TId));

            var table = _mapping.Schema.Table;

            _selectFields = table.SelectColumns(storageStyle).Select(x => $"d.{x.Name}").ToArray();
            var fieldSelector = _selectFields.Join(", ");
            _selectClause = $"select {fieldSelector} from {document.TableName.QualifiedName} as d";

            _loaderSql =
                $"select {fieldSelector} from {document.TableName.QualifiedName} as d where id = :id";

            _loadArraySql =
                $"select {fieldSelector} from {document.TableName.QualifiedName} as d where id = ANY(:ids)";

            if (document.TenancyStyle == TenancyStyle.Conjoined)
            {
                _loaderSql += $" and {CurrentTenantFilter.Filter}";
                _loadArraySql += $" and {CurrentTenantFilter.Filter}";
            }

            UseOptimisticConcurrency = document.UseOptimisticConcurrency;


            _setter = LambdaBuilder.Setter<T, TId>(document.IdMember);

            DeleteFragment = _mapping.DeleteStyle == DeleteStyle.Remove
                ? (IOperationFragment) new HardDelete(this)
                : new SoftDelete(this);

            HardDeleteFragment = new HardDelete(this);

            DuplicatedFields = _mapping.DuplicatedFields;
        }

        public void TruncateDocumentStorage(ITenant tenant)
        {
            var sql = "truncate {0} cascade".ToFormat(TableName.QualifiedName);
            tenant.RunSql(sql);
        }

        public Task TruncateDocumentStorageAsync(ITenant tenant)
        {
            var sql = "truncate {0} cascade".ToFormat(TableName.QualifiedName);
            return tenant.RunSqlAsync(sql);
        }

        public void SetIdentity(T document, TId identity)
        {
            _setter(document, identity);
        }

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

        private IEnumerable<ISqlFragment> extraFilters(ISqlFragment query)
        {
            if (_mapping.DeleteStyle == DeleteStyle.SoftDelete && !query.Contains(SchemaConstants.DeletedColumn))
            {
                yield return ExcludeSoftDeletedFilter.Instance;
            }

            if (TenancyStyle == TenancyStyle.Conjoined && !query.SpecifiesTenant())
            {
                yield return CurrentTenantFilter.Instance;
            }
        }

        private IEnumerable<ISqlFragment> defaultFilters()
        {
            if (_mapping.DeleteStyle == DeleteStyle.SoftDelete) yield return ExcludeSoftDeletedFilter.Instance;

            if (TenancyStyle == TenancyStyle.Conjoined) yield return CurrentTenantFilter.Instance;
        }


        public TenancyStyle TenancyStyle => _mapping.TenancyStyle;

        public Type DocumentType => _mapping.DocumentType;

        public DuplicatedField[] DuplicatedFields { get; }

        public ISqlFragment ByIdFilter(TId id)
        {
            return new ByIdFilter<TId>(id, _idType);
        }

        public IDeletion HardDeleteForId(TId id, ITenant tenant)
        {
            if (TenancyStyle == TenancyStyle.Conjoined)
            {
                return new Deletion(this, HardDeleteFragment)
                {
                    Where = CompoundWhereFragment.And(new SpecificTenantFilter(tenant), ByIdFilter(id)),
                    Id = id
                };
            }

            return new Deletion(this, HardDeleteFragment)
            {
                Where = ByIdFilter(id),
                Id = id
            };
        }

        public void EjectById(IMartenSession session, object id)
        {
            var typedId = (TId)id;

            if (session.ItemMap.TryGetValue(typeof(T), out var dict))
            {
                if (dict is Dictionary<TId, T> d) d.Remove(typedId);
            }
        }

        public void RemoveDirtyTracker(IMartenSession session, object id)
        {
            session.ChangeTrackers.RemoveAll(x =>
            {
                if (x is T doc)
                {
                    return Identity(doc).Equals(id);
                }

                return false;
            });
        }

        public IDeletion HardDeleteForDocument(T document, ITenant tenant)
        {
            var id = Identity(document);

            var deletion = HardDeleteForId(id, tenant);
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


        public abstract TId AssignIdentity(T document, ITenant tenant);

        public DbObjectName TableName { get; }

        string ISelectClause.FromObject => TableName.QualifiedName;

        public Type IdType => typeof(TId);

        public Guid? VersionFor(T document, IMartenSession session)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return session.Versions.VersionFor<T, TId>(Identity(document));
        }

        public abstract void Store(IMartenSession session, T document);
        public abstract void Store(IMartenSession session, T document, Guid? version);
        public abstract void Eject(IMartenSession session, T document);
        public abstract IStorageOperation Update(T document, IMartenSession session, ITenant tenant);
        public abstract IStorageOperation Insert(T document, IMartenSession session, ITenant tenant);
        public abstract IStorageOperation Upsert(T document, IMartenSession session, ITenant tenant);

        public abstract IStorageOperation Overwrite(T document, IMartenSession session, ITenant tenant);

        public IDeletion DeleteForDocument(T document, ITenant tenant)
        {
            var id = Identity(document);

            var deletion = DeleteForId(id, tenant);
            deletion.Document = document;

            return deletion;
        }

        public IDeletion DeleteForId(TId id, ITenant tenant)
        {
            if (TenancyStyle == TenancyStyle.Conjoined)
            {
                return new Deletion(this, DeleteFragment)
                {
                    Where = CompoundWhereFragment.And(new SpecificTenantFilter(tenant), ByIdFilter(id)),
                    Id = id
                };
            }

            return new Deletion(this, DeleteFragment)
            {
                Where = ByIdFilter(id),
                Id = id
            };
        }

        public IOperationFragment DeleteFragment { get; }

        public IOperationFragment HardDeleteFragment { get; }

        public ISqlFragment FilterDocuments(QueryModel? model, ISqlFragment query)
        {
            var extras = extraFilters(query).ToList();

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

        public IFieldMapping Fields { get; }


        public abstract T? Load(TId id, IMartenSession session);
        public abstract Task<T?> LoadAsync(TId id, IMartenSession session, CancellationToken token);


        protected T? load(TId id, IMartenSession session)
        {
            var command = BuildLoadCommand(id, session.Tenant);
            var selector = (ISelector<T>)BuildSelector(session);

            return session.Database.LoadOne(command, selector);
        }

        protected Task<T?> loadAsync(TId id, IMartenSession session, CancellationToken token)
        {
            var command = BuildLoadCommand(id, session.Tenant);
            var selector = (ISelector<T>)BuildSelector(session);

            return session.Database.LoadOneAsync(command, selector, token);
        }

        public abstract IReadOnlyList<T> LoadMany(TId[] ids, IMartenSession session);
        public abstract Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IMartenSession session, CancellationToken token);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract TId Identity(T document);


        public void WriteSelectClause(CommandBuilder sql)
        {
            sql.Append(_selectClause);
        }

        public string[] SelectFields()
        {
            return _selectFields;
        }

        public abstract ISelector BuildSelector(IMartenSession session);

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract NpgsqlCommand BuildLoadCommand(TId id, ITenant tenant);

        public abstract NpgsqlCommand BuildLoadManyCommand(TId[] ids, ITenant tenant);
    }
}
