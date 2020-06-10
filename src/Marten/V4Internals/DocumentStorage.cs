using System;
using System.Runtime.CompilerServices;
using Baseline;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Schema;
using Marten.Util;
using Remotion.Linq;

namespace Marten.V4Internals
{
    public abstract class DocumentStorage<T, TId>: IDocumentStorage<T, TId>
    {
        private readonly IWhereFragment _defaultWhere;
        private readonly IQueryableDocument _document;
        private string _selectClause;

        public DocumentStorage(IQueryableDocument document)
        {
            _document = document;
            Fields = document;
            TableName = document.Table;

            _defaultWhere = document.DefaultWhereFragment();

            _selectClause = $"select {_document.SelectFields().Join(", ")} from {document.Table.QualifiedName} as d";

        }

        public DbObjectName TableName { get; }
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
        public abstract IQueryHandler<T> Load(TId id);
        public abstract IQueryHandler<T> LoadMany(TId[] ids);

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
    }
}
