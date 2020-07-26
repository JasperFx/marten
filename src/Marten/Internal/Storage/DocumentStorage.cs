using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Internal.Linq;
using Marten.Internal.Operations;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Schema;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using Remotion.Linq;
using LambdaBuilder = Baseline.Expressions.LambdaBuilder;

namespace Marten.Internal.Storage
{
    public abstract class DocumentStorage<T, TId>: IDocumentStorage<T, TId>
    {
        private readonly IWhereFragment _defaultWhere;
        private readonly IQueryableDocument _document;
        private readonly string _selectClause;

        protected readonly string _loadArraySql;
        protected readonly string _loaderSql;
        protected Action<T, TId> _setter;
        protected readonly DocumentMapping _mapping;

        public DocumentStorage(DocumentMapping document)
        {
            _mapping = document;

            _document = document;
            Fields = document;
            TableName = document.Table;

            _defaultWhere = document.DefaultWhereFragment();

            _selectClause = $"select {_document.SelectFields().Select(x => $"d.{x}").Join(", ")} from {document.Table.QualifiedName} as d";

            _loaderSql =
                $"select {document.SelectFields().Join(", ")} from {document.Table.QualifiedName} as d where id = :id";

            _loadArraySql =
                $"select {document.SelectFields().Join(", ")} from {document.Table.QualifiedName} as d where id = ANY(:ids)";

            if (document.TenancyStyle == TenancyStyle.Conjoined)
            {
                _loaderSql += $" and {TenantWhereFragment.Filter}";
                _loadArraySql += $" and {TenantWhereFragment.Filter}";
            }

            QueryableDocument = document;

            UseOptimisticConcurrency = document.UseOptimisticConcurrency;


            _setter = LambdaBuilder.Setter<T, TId>(document.IdMember);
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

        public bool UseOptimisticConcurrency { get; }

        object IDocumentStorage<T>.IdentityFor(T document)
        {
            return Identity(document);
        }

        public Type SelectedType => typeof(T);

        public IQueryableDocument QueryableDocument { get; }

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
        public abstract IStorageOperation DeleteForDocument(T document);
        public abstract IStorageOperation DeleteForWhere(IWhereFragment where);

        public IWhereFragment FilterDocuments(QueryModel model, IWhereFragment query)
        {
            return _document.FilterDocuments(model, query);
        }

        public IWhereFragment DefaultWhereFragment()
        {
            return _defaultWhere;
        }

        public IFieldMapping Fields { get; }

        public abstract IStorageOperation DeleteForId(TId id);

        public abstract T Load(TId id, IMartenSession session);
        public abstract Task<T> LoadAsync(TId id, IMartenSession session, CancellationToken token);


        protected T load(TId id, IMartenSession session)
        {
            var command = BuildLoadCommand(id, session.Tenant);
            using (var reader = session.Database.ExecuteReader(command))
            {
                if (!reader.Read()) return default(T);

                var selector = (ISelector<T>)BuildSelector(session);
                return selector.Resolve(reader);
            }
        }

        protected async Task<T> loadAsync(TId id, IMartenSession session, CancellationToken token)
        {
            var command = BuildLoadCommand(id, session.Tenant);
            using (var reader = await session.Database.ExecuteReaderAsync(command, token).ConfigureAwait(false))
            {
                if (!(await reader.ReadAsync(token).ConfigureAwait(false))) return default(T);

                var selector = (ISelector<T>)BuildSelector(session);
                return await selector.ResolveAsync(reader, token).ConfigureAwait(false);
            }
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
            return _document.SelectFields();
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
