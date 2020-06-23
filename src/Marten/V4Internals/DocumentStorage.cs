using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Schema;
using Marten.Storage;
using Marten.Util;
using Marten.V4Internals.Linq;
using Npgsql;
using Remotion.Linq;

namespace Marten.V4Internals
{
    public abstract class DocumentStorage<T, TId>: IDocumentStorage<T, TId>
    {
        private readonly IWhereFragment _defaultWhere;
        private readonly IQueryableDocument _document;
        private readonly string _selectClause;

        protected readonly string _loadArraySql;
        protected readonly string _loaderSql;

        public DocumentStorage(DocumentMapping document)
        {
            _document = document;
            Fields = document;
            TableName = document.Table;

            _defaultWhere = document.DefaultWhereFragment();

            _selectClause = $"select {_document.SelectFields().Join(", ")} from {document.Table.QualifiedName} as d";

            _loaderSql =
                $"select {document.SelectFields().Join(", ")} from {document.Table.QualifiedName} as d where id = :id";

            _loadArraySql =
                $"select {document.SelectFields().Join(", ")} from {document.Table.QualifiedName} as d where id = ANY(:ids)";

            if (document.TenancyStyle == TenancyStyle.Conjoined)
            {
                _loaderSql += $" and {TenantWhereFragment.Filter}";
                _loadArraySql += $" and {TenantWhereFragment.Filter}";
            }

        }

        public Type SourceType => typeof(T);

        public abstract TId AssignIdentity(T document, ITenant tenant);

        public DbObjectName TableName { get; }

        string ISelectClause.FromObject => TableName.QualifiedName;

        public Type IdType => typeof(T);

        public Guid? VersionFor(T document, IMartenSession session)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return session.Versions.VersionFor<T, TId>(Identity(document));
        }

        public abstract void Store(IMartenSession session, T document);
        public abstract void Store(IMartenSession session, T document, Guid? version);
        public abstract void Eject(IMartenSession session, T document);
        public abstract IStorageOperation Update(T document, IMartenSession session);
        public abstract IStorageOperation Insert(T document, IMartenSession session);
        public abstract IStorageOperation Upsert(T document, IMartenSession session);
        public abstract IStorageOperation Overwrite(T document, IMartenSession session);
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




        public T Load(TId id, IMartenSession session)
        {
            var command = BuildLoadCommand(id, session.Tenant);
            using (var reader = session.Database.ExecuteReader(command))
            {
                if (!reader.Read()) return default(T);

                var selector = (ISelector<T>)BuildSelector(session);
                return selector.Resolve(reader);
            }
        }

        public async Task<T> LoadAsync(TId id, IMartenSession session, CancellationToken token)
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


        public void WriteSelectClause(CommandBuilder sql, bool withStatistics)
        {
            if (withStatistics) throw new NotImplementedException("Come back to this");
            sql.Append(_selectClause);
        }

        public string[] SelectFields()
        {
            return _document.SelectFields();
        }

        public abstract ISelector BuildSelector(IMartenSession session);

        public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, Statement statement)
        {
            var selector = (ISelector<T>)BuildSelector(session);

            return LinqHandlerBuilder.BuildHandler<T, TResult>(selector, statement);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract NpgsqlCommand BuildLoadCommand(TId id, ITenant tenant);

        public abstract NpgsqlCommand BuildLoadManyCommand(TId[] ids, ITenant tenant);
    }
}
