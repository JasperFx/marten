using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services;
using Marten.Services.BatchQuerying;
using Npgsql;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    public interface ILoader
    {
        FetchResult<T> LoadDocument<T>(object id) where T : class;
        Task<FetchResult<T>> LoadDocumentAsync<T>(object id, CancellationToken token) where T : class;
    }

    public class QuerySession : IQuerySession, ILoader
    {
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;
        private readonly IManagedConnection _connection;
        private readonly IQueryParser _parser;
        private readonly IIdentityMap _identityMap;

        public QuerySession(IDocumentStore store, IDocumentSchema schema, ISerializer serializer, IManagedConnection connection, IQueryParser parser, IIdentityMap identityMap)
        {
            DocumentStore = store;
            _schema = schema;
            _serializer = serializer;
            _connection = connection;
            _parser = parser;
            _identityMap = identityMap;
        }

        public IDocumentStore DocumentStore { get; }

        public IJsonLoader Json => new JsonLoader(_connection, _schema);

        public IMartenQueryable<T> Query<T>()
        {
            var executor = new MartenQueryExecutor(_connection, _schema, _identityMap);

            var queryProvider = new MartenQueryProvider(typeof (MartenQueryable<>), _parser, executor);
            return new MartenQueryable<T>(queryProvider);
        }

        public IList<T> Query<T>(string sql, params object[] parameters)
        {
            var handler = new UserSuppliedQueryHandler<T>(_schema, _serializer, sql, parameters);
            return _connection.Fetch(handler, _identityMap.ForQuery(), null);
        }

        public Task<IList<T>> QueryAsync<T>(string sql, CancellationToken token, params object[] parameters)
        {
            var handler = new UserSuppliedQueryHandler<T>(_schema, _serializer, sql, parameters);
            return _connection.FetchAsync(handler, _identityMap.ForQuery(), null, token);
        }

        public IBatchedQuery CreateBatchQuery()
        {
            return new BatchedQuery(_connection, _schema, _identityMap.ForQuery(), this, _serializer);
        }

        private IDocumentStorage storage<T>()
        {
            return _schema.StorageFor(typeof (T));
        }

        public FetchResult<T> LoadDocument<T>(object id) where T : class
        {
            var storage = storage<T>();
            var resolver = storage.As<IResolver<T>>();

            var cmd = storage.LoaderCommand(id);
            return _connection.Execute(cmd, c =>
            {
                using (var reader = cmd.ExecuteReader())
                {
                    return resolver.Fetch(reader, _serializer);
                }
            });
        }

        public Task<FetchResult<T>> LoadDocumentAsync<T>(object id, CancellationToken token) where T : class
        {
            var storage = storage<T>();
            var resolver = storage.As<IResolver<T>>();

            var cmd = storage.LoaderCommand(id);

            return _connection.ExecuteAsync(cmd, async (c, tkn) =>
            {
                using (var reader = await cmd.ExecuteReaderAsync(tkn).ConfigureAwait(false))
                {
                    return await resolver.FetchAsync(reader, _serializer, token).ConfigureAwait(false);
                }
            }, token);
        }

        public T Load<T>(string id)
        {
            return load<T>(id);
        }

        public Task<T> LoadAsync<T>(string id, CancellationToken token)
        {
            return loadAsync<T>(id, token);
        }

        public T Load<T>(ValueType id)
        {
            return load<T>(id);
        }

        private T load<T>(object id)
        {
            return storage<T>().As<IResolver<T>>().Resolve(_identityMap, this, id);
        }

        private Task<T> loadAsync<T>(object id, CancellationToken token)
        {
            return storage<T>().As<IResolver<T>>().ResolveAsync(_identityMap, this, token, id);
        }

        public ILoadByKeys<T> LoadMany<T>()
        {
            return new LoadByKeys<T>(this);
        }




        public IList<T> LoadMany<T>(params string[] ids)
        {
            return LoadMany<T>().ById(ids);
        }

        public IList<T> LoadMany<T>(params Guid[] ids)
        {
            return LoadMany<T>().ById(ids);
        }

        public IList<T> LoadMany<T>(params int[] ids)
        {
            return LoadMany<T>().ById(ids);
        }

        public IList<T> LoadMany<T>(params long[] ids)
        {
            return LoadMany<T>().ById(ids);
        }

        public Task<IList<T>> LoadManyAsync<T>(params string[] ids)
        {
            return LoadMany<T>().ByIdAsync(ids);
        }

        public Task<IList<T>> LoadManyAsync<T>(params Guid[] ids)
        {
            return LoadMany<T>().ByIdAsync(ids);
        }

        public Task<IList<T>> LoadManyAsync<T>(params int[] ids)
        {
            return LoadMany<T>().ByIdAsync(ids);
        }

        public Task<IList<T>> LoadManyAsync<T>(params long[] ids)
        {
            return LoadMany<T>().ByIdAsync(ids);
        }

        public Task<IList<T>> LoadManyAsync<T>(CancellationToken token, params string[] ids)
        {
            return LoadMany<T>().ByIdAsync(ids, token);
        }

        public Task<IList<T>> LoadManyAsync<T>(CancellationToken token, params Guid[] ids)
        {
            return LoadMany<T>().ByIdAsync(ids, token);
        }

        public Task<IList<T>> LoadManyAsync<T>(CancellationToken token, params int[] ids)
        {
            return LoadMany<T>().ByIdAsync(ids, token);
        }

        public Task<IList<T>> LoadManyAsync<T>(CancellationToken token, params long[] ids)
        {
            return LoadMany<T>().ByIdAsync(ids, token);
        }


        private class LoadByKeys<TDoc> : ILoadByKeys<TDoc>
        {
            private readonly QuerySession _parent;

            public LoadByKeys(QuerySession parent)
            {
                _parent = parent;
            }

            public IList<TDoc> ById<TKey>(params TKey[] keys)
            {
                var hitsAndMisses = this.hitsAndMisses(keys);
                var hits = hitsAndMisses.Item1;
                var misses = hitsAndMisses.Item2;
                var documents = fetchDocuments(misses);

                return concatDocuments(hits, documents);
            }

            public Task<IList<TDoc>> ByIdAsync<TKey>(params TKey[] keys)
            {
                return ByIdAsync(keys, CancellationToken.None);
            }

            public IList<TDoc> ById<TKey>(IEnumerable<TKey> keys)
            {
                return ById(keys.ToArray());
            }

            public async Task<IList<TDoc>> ByIdAsync<TKey>(IEnumerable<TKey> keys,
                CancellationToken token = default(CancellationToken))
            {
                var hitsAndMisses = this.hitsAndMisses(keys.ToArray());
                var hits = hitsAndMisses.Item1;
                var misses = hitsAndMisses.Item2;
                var documents = await fetchDocumentsAsync(misses, token).ConfigureAwait(false);

                return concatDocuments(hits, documents);
            }

            private IList<TDoc> concatDocuments<TKey>(TKey[] hits, IEnumerable<TDoc> documents)
            {
                return
                    hits.Select(key => _parent._identityMap.Retrieve<TDoc>(key))
                        .Concat(documents)
                        .ToList();
            }

            private Tuple<TKey[], TKey[]> hitsAndMisses<TKey>(TKey[] keys)
            {
                var hits = keys.Where(key => _parent._identityMap.Has<TDoc>(key)).ToArray();
                var misses = keys.Where(x => !hits.Contains(x)).ToArray();
                return new Tuple<TKey[], TKey[]>(hits, misses);
            }

            private IEnumerable<TDoc> fetchDocuments<TKey>(TKey[] keys)
            {
                var storage = _parent._schema.StorageFor(typeof (TDoc));
                var resolver = storage.As<IResolver<TDoc>>();
                var cmd = storage.LoadByArrayCommand(keys);

                var list = new List<TDoc>();

                _parent._connection.Execute(cmd, c =>
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var doc = resolver.Resolve(0, reader, _parent._identityMap);
                            list.Add(doc);
                        }
                    }
                });

                return list;
            }

            private async Task<IEnumerable<TDoc>> fetchDocumentsAsync<TKey>(TKey[] keys, CancellationToken token)
            {
                var storage = _parent._schema.StorageFor(typeof (TDoc));
                var resolver = storage.As<IResolver<TDoc>>();
                var cmd = storage.LoadByArrayCommand(keys);

                var list = new List<TDoc>();

                await _parent._connection.ExecuteAsync(cmd, async (conn, tkn) =>
                {
                    using (var reader = await cmd.ExecuteReaderAsync(tkn).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(tkn).ConfigureAwait(false))
                        {
                            var doc = resolver.Resolve(0, reader, _parent._identityMap);
                            list.Add(doc);
                        }
                    }
                }, token).ConfigureAwait(false);

                return list;
            }
        }

        public TOut Query<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query)
        {
            QueryStatistics stats;
            var handler = _schema.HandlerFactory.HandlerFor(query, out stats);
            return _connection.Fetch(handler, _identityMap.ForQuery(), stats);
        }

        public Task<TOut> QueryAsync<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query,
            CancellationToken token = new CancellationToken())
        {
            QueryStatistics stats;
            var handler = _schema.HandlerFactory.HandlerFor(query, out stats);
            return _connection.FetchAsync(handler, _identityMap.ForQuery(), stats, token);
        }

        public NpgsqlConnection Connection => _connection.Connection;

        public IMartenSessionLogger Logger
        {
            get { return _connection.As<ManagedConnection>().Logger; }
            set { _connection.As<ManagedConnection>().Logger = value; }
        }

        public int RequestCount => _connection.RequestCount;

        public void Dispose()
        {
            _connection.Dispose();
        }

        public T Load<T>(int id)
        {
            return load<T>(id);
        }

        public T Load<T>(long id)
        {
            return load<T>(id);
        }

        public T Load<T>(Guid id) 
        {
            return load<T>(id);
        }

        public Task<T> LoadAsync<T>(int id, CancellationToken token = new CancellationToken())
        {
            return loadAsync<T>(id, token);
        }

        public Task<T> LoadAsync<T>(long id, CancellationToken token = new CancellationToken())
        {
            return loadAsync<T>(id, token);
        }

        public Task<T> LoadAsync<T>(Guid id, CancellationToken token = new CancellationToken())
        {
            return loadAsync<T>(id, token);
        }


    }
}