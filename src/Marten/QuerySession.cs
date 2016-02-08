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
using Marten.Util;
using Npgsql;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    public class QuerySession : IQuerySession
    {
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;
        private readonly ICommandRunner _runner;
        private readonly IQueryParser _parser;
        private readonly IIdentityMap _identityMap;

        public QuerySession(IDocumentSchema schema, ISerializer serializer, ICommandRunner runner, IQueryParser parser, IIdentityMap identityMap)
        {
            _schema = schema;
            _serializer = serializer;
            _runner = runner;
            _parser = parser;
            _identityMap = identityMap;
        }

        public IQueryable<T> Query<T>()
        {
            var executor = new MartenQueryExecutor(_runner, _schema, _serializer, _parser, _identityMap);

            var queryProvider = new MartenQueryProvider(typeof(MartenQueryable<>), _parser, executor);
            return new MartenQueryable<T>(queryProvider);
        }

        public IEnumerable<T> Query<T>(string sql, params object[] parameters)
        {
            using (var cmd = BuildCommand<T>(sql, parameters))
            {
                return _runner.QueryJson(cmd)
                    .Select(json => _serializer.FromJson<T>(json))
                    .ToArray();
            }
        }

        public async Task<IEnumerable<T>> QueryAsync<T>(string sql, CancellationToken token, params object[] parameters)
        {
            using (var cmd = BuildCommand<T>(sql, parameters))
            {
                var result = await _runner.QueryJsonAsync(cmd, token).ConfigureAwait(false);
                return result
                    .Select(json => _serializer.FromJson<T>(json))
                    .ToArray();
            }
        }

        private NpgsqlCommand BuildCommand<T>(string sql, params object[] parameters)
        {
            var cmd = new NpgsqlCommand();
            var mapping = _schema.MappingFor(typeof(T));

            if (!sql.Contains("select"))
            {
                var tableName = mapping.TableName;
                sql = "select data from {0} {1}".ToFormat(tableName, sql);
            }

            parameters.Each(x =>
            {
                var param = cmd.AddParameter(x);
                sql = sql.UseParameter(param);
            });

            cmd.CommandText = sql;

            return cmd;
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
            return _identityMap.Get<T>(id, () =>
            {
                return findJsonById<T>(id);
            });
        }

        private Task<T> loadAsync<T>(object id, CancellationToken token) where T : class
        {
            return _identityMap.GetAsync<T>(id, getAsyncToken =>
            {
                return findJsonByIdAsync<T>(id, getAsyncToken);
            }, token);
        }

        public ILoadByKeys<T> Load<T>() where T : class
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

            return _runner.Execute(conn =>
            {
                var loader = storage.LoaderCommand(id);
                loader.Connection = conn;
                return loader.ExecuteScalar() as string; // Maybe do this as a stream later for big docs?
            });
        }

        private Task<string> findJsonByIdAsync<T>(object id, CancellationToken token)
        {
            var storage = _schema.StorageFor(typeof(T));

            return _runner.ExecuteAsync(async (conn, executeAsyncToken) =>
            {
                var loader = storage.LoaderCommand(id);
                loader.Connection = conn;
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

            public IEnumerable<TDoc> ById<TKey>(params TKey[] keys)
            {
                var hitsAndMisses = GetHitsAndMisses(keys);
                var hits = hitsAndMisses.Item1;
                var misses = hitsAndMisses.Item2;
                var documents = fetchDocuments(misses);

                return ConcatDocuments(hits, documents);
            }

            public async Task<IEnumerable<TDoc>> ByIdAsync<TKey>(params TKey[] keys)
            {
                return await ByIdAsync(keys, CancellationToken.None).ConfigureAwait(false);
            }

            public IEnumerable<TDoc> ById<TKey>(IEnumerable<TKey> keys)
            {
                return ById(keys.ToArray());
            }

            public async Task<IEnumerable<TDoc>> ByIdAsync<TKey>(IEnumerable<TKey> keys, CancellationToken token)
            {
                var hitsAndMisses = GetHitsAndMisses(keys.ToArray());
                var hits = hitsAndMisses.Item1;
                var misses = hitsAndMisses.Item2;
                var documents = await fetchDocumentsAsync(misses, token).ConfigureAwait(false);

                return ConcatDocuments(hits, documents);
            }

            private IEnumerable<TDoc> ConcatDocuments<TKey>(TKey[] hits, IEnumerable<TDoc> documents)
            {
                return
                    hits.Select(key => _parent._identityMap.Retrieve<TDoc>(key))
                        .Concat(documents)
                        .ToArray();
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

                _parent._runner.Execute(conn =>
                {
                    cmd.Connection = conn;
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

                await _parent._runner.ExecuteAsync(async (conn, tkn) =>
                {
                    cmd.Connection = conn;
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

        public void Dispose()
        {
            
        }
    }
}