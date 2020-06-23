using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Schema;
using Marten.Util;
using Marten.V4Internals.Linq;
using Remotion.Linq;

namespace Marten.V4Internals
{
    public class SubClassDocumentStorage<T, TRoot, TId>: IDocumentStorage<T, TId>
        where T : TRoot
    {
        private readonly IDocumentStorage<TRoot, TId> _parent;
        private readonly SubClassMapping _mapping;
        private readonly IWhereFragment _defaultWhere;

        public SubClassDocumentStorage(IDocumentStorage<TRoot, TId> parent, SubClassMapping mapping)
        {
            _parent = parent;
            _mapping = mapping;

            FromObject = _mapping.Table.QualifiedName;

            _defaultWhere = _mapping.DefaultWhereFragment();
        }

        public string FromObject { get; }
        public void WriteSelectClause(CommandBuilder sql, bool withStatistics)
        {
            _parent.WriteSelectClause(sql, withStatistics);
        }

        public string[] SelectFields()
        {
            return _mapping.SelectFields();
        }

        public ISelector BuildSelector(IMartenSession session)
        {
            var inner = _parent.BuildSelector(session);
            return new CastingSelector<T, TRoot>((ISelector<TRoot>) inner);
        }

        public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, Statement statement)
        {
            var selector = (ISelector<T>)BuildSelector(session);

            return LinqHandlerBuilder.BuildHandler<T, TResult>(selector, statement);
        }

        public Type SourceType => typeof(TRoot);
        public IFieldMapping Fields => _mapping;

        public IWhereFragment FilterDocuments(QueryModel model, IWhereFragment query)
        {
            return _mapping.FilterDocuments(model, query);
        }

        public IWhereFragment DefaultWhereFragment()
        {
            return _defaultWhere;
        }

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

        public IStorageOperation Update(T document, IMartenSession session)
        {
            return _parent.Update(document, session);
        }

        public IStorageOperation Insert(T document, IMartenSession session)
        {
            return _parent.Insert(document, session);
        }

        public IStorageOperation Upsert(T document, IMartenSession session)
        {
            return _parent.Upsert(document, session);
        }

        public IStorageOperation Overwrite(T document, IMartenSession session)
        {
            return _parent.Overwrite(document, session);
        }

        public IStorageOperation DeleteForDocument(T document)
        {
            return _parent.DeleteForDocument(document);
        }

        public IStorageOperation DeleteForWhere(IWhereFragment @where)
        {
            return _parent.DeleteForWhere(@where);
        }

        public IStorageOperation DeleteForId(TId id)
        {
            return _parent.DeleteForId(id);
        }

        public T Load(TId id, IMartenSession session)
        {
            return (T) _parent.Load(id, session);
        }

        public async Task<T> LoadAsync(TId id, IMartenSession session, CancellationToken token)
        {
            return (T) await _parent.LoadAsync(id, session, token);
        }

        public IReadOnlyList<T> LoadMany(TId[] ids, IMartenSession session)
        {
            return _parent.LoadMany(ids, session).OfType<T>().ToList();
        }

        public async Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IMartenSession session, CancellationToken token)
        {
            return (await _parent.LoadManyAsync(ids, session, token)).OfType<T>().ToList();
        }
    }
}
