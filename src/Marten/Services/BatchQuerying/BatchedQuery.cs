using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Linq.Results;
using Marten.Schema;
using Npgsql;

namespace Marten.Services.BatchQuerying
{
    public class BatchedQuery : IBatchedQuery
    {
        private static readonly MartenQueryParser QueryParser = new MartenQueryParser();
        private readonly IManagedConnection _runner;
        private readonly IDocumentSchema _schema;
        private readonly IIdentityMap _identityMap;
        private readonly QuerySession _parent;
        private readonly ISerializer _serializer;
        private readonly NpgsqlCommand _command = new NpgsqlCommand();
        private readonly IList<IBatchQueryItem> _items = new List<IBatchQueryItem>();

        public BatchedQuery(IManagedConnection runner, IDocumentSchema schema, IIdentityMap identityMap, QuerySession parent, ISerializer serializer)
        {
            _runner = runner;
            _schema = schema;
            _identityMap = identityMap;
            _parent = parent;
            _serializer = serializer;
        }

        public Task<T> Load<T>(string id) where T : class
        {
            return load<T>(id);
        }

        public Task<T> Load<T>(ValueType id) where T : class
        {
            return load<T>(id);
        }

        public Task<T> AddItem<T>(IQueryHandler<T> handler)
        {
            _schema.EnsureStorageExists(handler.SourceType);

            var item = new BatchQueryItem<T>(handler);
            _items.Add(item);

            item.Configure(_schema, _command);

            return item.Result;
        }

        private Task<T> load<T>(object id) where T : class
        {
            if (_identityMap.Has<T>(id))
            {
                return Task.FromResult(_identityMap.Retrieve<T>(id));
            }

            return AddItem(new LoadByIdHandler<T>(_schema.ResolverFor<T>(), _schema.MappingFor(typeof (T)), id));
        }


        public IBatchLoadByKeys<TDoc> LoadMany<TDoc>() where TDoc : class
        {
            return new BatchLoadByKeys<TDoc>(this);
        }

        public class BatchLoadByKeys<TDoc> : IBatchLoadByKeys<TDoc> where TDoc : class
        {
            private readonly BatchedQuery _parent;

            public BatchLoadByKeys(BatchedQuery parent)
            {
                _parent = parent;
            }

            private Task<IList<TDoc>> load<TKey>(TKey[] keys)
            {
                var resolver = _parent._schema.ResolverFor<TDoc>();
                var mapping = _parent._schema.MappingFor(typeof (TDoc));

                return _parent.AddItem(new LoadByIdArrayHandler<TDoc, TKey>(resolver, mapping, keys));
            }

            public Task<IList<TDoc>> ById<TKey>(params TKey[] keys)
            {
                return load(keys);
            }

            public Task<IList<TDoc>> ByIdList<TKey>(IEnumerable<TKey> keys)
            {
                return load(keys.ToArray());
            }
        }

        public Task<IList<T>> Query<T>(string sql, params object[] parameters) where T : class
        {
            return AddItem(new UserSuppliedQueryHandler<T>(_schema, _serializer, sql, parameters));
        }

        [Obsolete("This is going to go away, but watch the includes")]
        private DocumentQuery toDocumentQuery<TDoc>(IMartenQueryable<TDoc> queryable)
        {
            var expression = queryable.Expression;

            var model = QueryParser.GetParsedQuery(expression);

            var docQuery = _schema.ToDocumentQuery(model);
            docQuery.Includes.AddRange(queryable.Includes);


            return docQuery;
        }


        public Task<bool> Any<TDoc>(IMartenQueryable<TDoc> queryable)
        {
            var expression = queryable.Expression;

            var query = QueryParser.GetParsedQuery(expression);

            return AddItem(new AnyQueryHandler<TDoc>(query, _schema));
        }


        public Task<long> Count<TDoc>(IMartenQueryable<TDoc> queryable)
        {
            var expression = queryable.Expression;

            var query = QueryParser.GetParsedQuery(expression);

            return AddItem(new CountQueryHandler<long>(query, _schema));
        }

        internal Task<IList<T>> Query<T>(IMartenQueryable<T> queryable)
        {
            var expression = queryable.Expression;

            var query = QueryParser.GetParsedQuery(expression);

            return AddItem(new ListQueryHandler<T>(_schema, query, queryable.Includes.ToArray()));
        }

        public IBatchedQueryable<T> Query<T>() where T : class
        {
            return new BatchedQueryable<T>(this, _parent.Query<T>());
        }


        public Task<T> First<T>(IMartenQueryable<T> queryable)
        {
            var expression = queryable.Expression;

            var query = QueryParser.GetParsedQuery(expression);

            return AddItem(OneResultHandler<T>.First(_schema, query, queryable.Includes.ToArray()));
        }

        public Task<T> FirstOrDefault<T>(IMartenQueryable<T> queryable)
        {
            var expression = queryable.Expression;

            var query = QueryParser.GetParsedQuery(expression);

            return AddItem(OneResultHandler<T>.FirstOrDefault(_schema, query, queryable.Includes.ToArray()));
        }

        public Task<T> Single<T>(IMartenQueryable<T> queryable)
        {
            var expression = queryable.Expression;

            var query = QueryParser.GetParsedQuery(expression);

            return AddItem(OneResultHandler<T>.Single(_schema, query, queryable.Includes.ToArray()));
        }

        public Task<T> SingleOrDefault<T>(IMartenQueryable<T> queryable)
        {
            var expression = queryable.Expression;

            var query = QueryParser.GetParsedQuery(expression);

            return AddItem(OneResultHandler<T>.SingleOrDefault(_schema, query, queryable.Includes.ToArray()));
        }

        public Task Execute(CancellationToken token = default(CancellationToken))
        {
            var map = _identityMap.ForQuery();

            if (!_items.Any()) return Task.CompletedTask;

            return _runner.ExecuteAsync(_command, async (cmd, tk) =>
            {
                using (var reader = await _command.ExecuteReaderAsync(tk).ConfigureAwait(false))
                {
                    // TODO -- this will be async later
                    _items[0].Read(reader, map);

                    var others = _items.Skip(1).ToArray();

                    foreach (var item in others)
                    {
                        var hasNext = await reader.NextResultAsync(token).ConfigureAwait(false);

                        if (!hasNext)
                        {
                            throw new InvalidOperationException("There is no next result to read over.");
                        }

                        // TODO -- needs to be purely async later
                        item.Read(reader, map);
                    }
                }

                return 0;
            }, token);
        }
    }
}