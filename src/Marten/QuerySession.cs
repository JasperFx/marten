using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Services.BatchQuerying;
using Marten.Util;
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

        public QuerySession(IDocumentSchema schema, ISerializer serializer, IManagedConnection connection, IQueryParser parser, IIdentityMap identityMap, StoreOptions options)
        {
            _schema = schema;
            _serializer = serializer;
            _connection = connection;
            _parser = parser;
            _identityMap = identityMap;

            Parser = new MartenExpressionParser(_serializer, options);
        }

        internal MartenExpressionParser Parser { get; }

        public IMartenQueryable<T> Query<T>()
        {
            var executor = new MartenQueryExecutor(_connection, _schema, Parser, _parser, _identityMap);

            var queryProvider = new MartenQueryProvider(typeof(MartenQueryable<>), _parser, executor);
            return new MartenQueryable<T>(queryProvider);
        }

        public IEnumerable<T> Query<T>(string sql, params object[] parameters)
        {
            using (var cmd = BuildCommand<T>(sql, parameters))
            {
                return _connection.QueryJson(cmd)
                    .Select(json => _serializer.FromJson<T>(json))
                    .ToArray();
            }
        }

        public async Task<IEnumerable<T>> QueryAsync<T>(string sql, CancellationToken token, params object[] parameters)
        {
            using (var cmd = BuildCommand<T>(sql, parameters))
            {
                var result = await _connection.QueryJsonAsync(cmd, token).ConfigureAwait(false);
                return result
                    .Select(json => _serializer.FromJson<T>(json))
                    .ToArray();
            }
        }

        public IBatchedQuery CreateBatchQuery()
        {
            return new BatchedQuery(_connection, _schema, _identityMap, this, _serializer, Parser);
        }

        public NpgsqlCommand BuildCommand<T>(string sql, params object[] parameters)
        {
            var cmd = new NpgsqlCommand();

            ConfigureCommand<T>(cmd, sql, parameters);

            return cmd;
        }

        public void ConfigureCommand<T>(NpgsqlCommand cmd, string sql, object[] parameters)
        {
            if (!sql.Contains("select", StringComparison.OrdinalIgnoreCase))
            {
                var mapping = _schema.MappingFor(typeof(T));
                var tableName = mapping.TableName;
                sql = "select data from {0} {1}".ToFormat(tableName, sql);
            }

            parameters.Each(x =>
            {
                var param = cmd.AddParameter(x);
                sql = sql.UseParameter(param);
            });

            cmd.AppendQuery(sql);
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
                    var found = reader.Read();
                    return found ? new FetchResult<T>(resolver.Build(reader, _serializer), reader.GetString(0)) : null;
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
                    var found = await reader.ReadAsync(tkn).ConfigureAwait(false);
                    return found ? new FetchResult<T>(resolver.Build(reader, _serializer), reader.GetString(0)) : null;
                }

            }, token);
        }
        
        public T Load<T>(string id) where T : class
        {
            return load<T>(id);
        }

        public Task<T> LoadAsync<T>(string id, CancellationToken token) where T : class
        {
            return loadAsync<T>(id, token);
        }

        public T Load<T>(ValueType id) where T : class
        {
            return load<T>(id);
        }

        public Task<T> LoadAsync<T>(ValueType id, CancellationToken token) where T : class
        {
            return loadAsync<T>(id, token);
        }

        private T load<T>(object id) where T : class
        {
            return storage<T>().As<IResolver<T>>().Resolve(_identityMap, this, id);
        }

        private Task<T> loadAsync<T>(object id, CancellationToken token) where T : class
        {
            return storage<T>().As<IResolver<T>>().ResolveAsync(_identityMap, this, token, id);
        }

        public ILoadByKeys<T> LoadMany<T>() where T : class
        {
            return new LoadByKeys<T>(this);
        }

        public string FindJsonById<T>(string id) where T : class
        {
            return findJsonById<T>(id);
        }

        public string FindJsonById<T>(ValueType id) where T : class
        {
            return findJsonById<T>(id);
        }

        public Task<string> FindJsonByIdAsync<T>(string id, CancellationToken token) where T : class
        {
            return findJsonByIdAsync<T>(id, token);
        }

        public Task<string> FindJsonByIdAsync<T>(ValueType id, CancellationToken token) where T : class
        {
            return findJsonByIdAsync<T>(id, token);
        }

        private string findJsonById<T>(object id)
        {
            var storage = _schema.StorageFor(typeof(T));

            var loader = storage.LoaderCommand(id);
            return _connection.Execute(loader, c => loader.ExecuteScalar() as string);
        }

        private Task<string> findJsonByIdAsync<T>(object id, CancellationToken token)
        {
            var storage = _schema.StorageFor(typeof(T));

            var loader = storage.LoaderCommand(id);
            return _connection.ExecuteAsync(loader, async (conn, executeAsyncToken) =>
            {
                var result = await loader.ExecuteScalarAsync(executeAsyncToken).ConfigureAwait(false);
                return result as string; // Maybe do this as a stream later for big docs?
            }, token);
        }

        private class LoadByKeys<TDoc> : ILoadByKeys<TDoc> where TDoc : class
        {
            private readonly QuerySession _parent;

            public LoadByKeys(QuerySession parent)
            {
                _parent = parent;
            }

            public IList<TDoc> ById<TKey>(params TKey[] keys)
            {
                var hitsAndMisses = GetHitsAndMisses(keys);
                var hits = hitsAndMisses.Item1;
                var misses = hitsAndMisses.Item2;
                var documents = fetchDocuments(misses);

                return ConcatDocuments(hits, documents);
            }

            public Task<IList<TDoc>> ByIdAsync<TKey>(params TKey[] keys)
            {
                return ByIdAsync(keys, CancellationToken.None);
            }

            public IList<TDoc> ById<TKey>(IEnumerable<TKey> keys)
            {
                return ById(keys.ToArray());
            }

            public async Task<IList<TDoc>> ByIdAsync<TKey>(IEnumerable<TKey> keys, CancellationToken token = default(CancellationToken))
            {
                var hitsAndMisses = GetHitsAndMisses(keys.ToArray());
                var hits = hitsAndMisses.Item1;
                var misses = hitsAndMisses.Item2;
                var documents = await fetchDocumentsAsync(misses, token).ConfigureAwait(false);

                return ConcatDocuments(hits, documents);
            }

            private IList<TDoc> ConcatDocuments<TKey>(TKey[] hits, IEnumerable<TDoc> documents)
            {
                return
                    hits.Select(key => _parent._identityMap.Retrieve<TDoc>(key))
                        .Concat(documents)
                        .ToList();
            }

            private Tuple<TKey[], TKey[]> GetHitsAndMisses<TKey>(TKey[] keys)
            {
                var hits = keys.Where(key => _parent._identityMap.Has<TDoc>(key)).ToArray();
                var misses = keys.Where(x => !hits.Contains(x)).ToArray();
                return new Tuple<TKey[], TKey[]>(hits, misses);
            }

            private IEnumerable<TDoc> fetchDocuments<TKey>(TKey[] keys)
            {
                var storage = _parent._schema.StorageFor(typeof(TDoc));
                var resolver = storage.As<IResolver<TDoc>>();
                var cmd = storage.LoadByArrayCommand(keys);

                var list = new List<TDoc>();

                _parent._connection.Execute(cmd, c =>
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var doc = resolver.Resolve(reader, _parent._identityMap);
                            list.Add(doc);
                        }
                    }
                });

                return list;
            }

            private async Task<IEnumerable<TDoc>> fetchDocumentsAsync<TKey>(TKey[] keys, CancellationToken token)
            {
                var storage = _parent._schema.StorageFor(typeof(TDoc));
                var resolver = storage.As<IResolver<TDoc>>();
                var cmd = storage.LoadByArrayCommand(keys);

                var list = new List<TDoc>();

                await _parent._connection.ExecuteAsync(cmd, async (conn, tkn) =>
                {
                    using (var reader = await cmd.ExecuteReaderAsync(tkn).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(tkn).ConfigureAwait(false))
                        {
                            var doc = resolver.Resolve(reader, _parent._identityMap);
                            list.Add(doc);
                        }
                    }
                }, token).ConfigureAwait(false);

                return list;
            }

        }

        public NpgsqlConnection Connection => _connection.Connection;

        public IMartenSessionLogger Logger
        {
            get { return _connection.As<ManagedConnection>().Logger; }
            set { _connection.As<ManagedConnection>().Logger = value; }
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }
}