using System;
using System.Collections.Generic;
using System.Linq;
using FubuCore;
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
        private readonly IMartenQueryExecutor _executor;
        private readonly IIdentityMap _documentMap;

        public QuerySession(IDocumentSchema schema, ISerializer serializer, ICommandRunner runner, IQueryParser parser, IMartenQueryExecutor executor, IIdentityMap documentMap)
        {
            _schema = schema;
            _serializer = serializer;
            _runner = runner;
            _parser = parser;
            _executor = executor;
            _documentMap = documentMap;
        }

        public IQueryable<T> Query<T>()
        {
            return new MartenQueryable<T>(_parser, _executor);
        }

        public IEnumerable<T> Query<T>(string sql, params object[] parameters)
        {
            var mapping = _schema.MappingFor(typeof(T));

            if (!sql.Contains("select"))
            {
                var tableName = mapping.TableName;
                sql = "select data from {0} {1}".ToFormat(tableName, sql);
            }

            using (var cmd = new NpgsqlCommand())
            {
                parameters.Each(x =>
                {
                    var param = cmd.AddParameter(x);
                    sql = sql.UseParameter(param);
                });

                cmd.CommandText = sql;

                return _runner.QueryJson(cmd)
                    .Select(json => _serializer.FromJson<T>(json))
                    .ToArray();
            }
        }
        
        public T Load<T>(string id) where T : class
        {
            return load<T>(id);
        }

        public T Load<T>(ValueType id) where T : class
        {
            return load<T>(id);
        }

        private T load<T>(object id) where T : class
        {
            return _documentMap.Get<T>(id, () =>
            {
                var storage = _schema.StorageFor(typeof(T));

                return _runner.Execute(conn =>
                {
                    var loader = storage.LoaderCommand(id);
                    loader.Connection = conn;
                    return loader.ExecuteScalar() as string; // Maybe do this as a stream later for big docs?
                });
            });

        }

        public ILoadByKeys<T> Load<T>() where T : class
        {
            return new LoadByKeys<T>(this);
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
                var hits = keys.Where(key => _parent._documentMap.Has<TDoc>(key)).ToArray();
                var misses = keys.Where(x => !hits.Contains(x)).ToArray();


                return
                    hits.Select(key => _parent._documentMap.Retrieve<TDoc>(key))
                        .Concat(fetchDocuments(misses))
                        .ToArray();
            }

            private IEnumerable<TDoc> fetchDocuments<TKey>(TKey[] keys)
            {
                var storage = _parent._schema.StorageFor(typeof(TDoc));
                var cmd = storage.LoadByArrayCommand(keys);

                var list = new List<TDoc>();

                _parent._runner.Execute(conn =>
                {
                    cmd.Connection = conn;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var id = reader[1];
                            var json = reader.GetString(0);

                            var doc = _parent._documentMap.Get<TDoc>(id, json);

                            list.Add(doc);
                        }
                    }
                });

                return list;
            }

            public IEnumerable<TDoc> ById<TKey>(IEnumerable<TKey> keys)
            {
                return ById(keys.ToArray());
            }
        }

        public void Dispose()
        {
            
        }
    }
}