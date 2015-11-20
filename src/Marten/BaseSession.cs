using System;
using System.Collections.Generic;
using System.Linq;
using FubuCore;
using Marten.Linq;
using Marten.Map;
using Marten.Schema;
using Marten.Util;
using Npgsql;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    public abstract class BaseSession : ISession
    {
        private readonly IQueryParser _parser;
        private readonly IMartenQueryExecutor _executor;
        private readonly IDocumentMap _documentMap;
        private readonly ICommandRunner _runner;
        private readonly ISerializer _serializer;
        private readonly IDocumentSchema _schema;

        protected BaseSession(IDocumentSchema schema, ISerializer serializer, ICommandRunner runner, IQueryParser parser, IMartenQueryExecutor executor, IDocumentMap documentMap, IDiagnostics diagnostics)
        {
            _schema = schema;
            _serializer = serializer;
            _runner = runner;

            _parser = parser;
            _executor = executor;
            _documentMap = documentMap;
            Diagnostics = diagnostics;
        }

        public void Dispose()
        {
        }

        public void Delete<T>(T entity)
        {
            _documentMap.DeleteDocument(entity);
        }

        public void Delete<T>(ValueType id)
        {
            _documentMap.DeleteById<T>(id);
        }

        public void Delete<T>(string id)
        {
            _documentMap.DeleteById<T>(id);
        }

        public T Load<T>(string id)
        {
            return LoadEntity<T>(id);
        }

        public T Load<T>(ValueType id)
        {
            return LoadEntity<T>(id);
        }

        public void Store<T>(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var id =_schema.StorageFor(typeof(T))
                .As<IdAssignment<T>>().Assign(entity);

            _documentMap.Store(id, entity);
        }

        public IQueryable<T> Query<T>()
        {
            return new MartenQueryable<T>(_parser, _executor);
        }

        public IEnumerable<T> Query<T>(string sql, params object[] parameters)
        {
            var mapping = _schema.MappingFor(typeof (T));

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
                    .Select(SerializeLoadedDocument<T>)
                    .ToArray();
            }
        }

        public void BulkInsert<T>(T[] documents, int batchSize = 1000)
        {
            var storage = _schema.StorageFor(typeof (T)).As<IBulkLoader<T>>();

            _runner.ExecuteInTransaction(conn =>
            {
                if (documents.Length <= batchSize)
                {
                    storage.Load(_serializer, conn, documents);
                }
                else
                {
                    var total = 0;
                    var page = 0;

                    while (total < documents.Length)
                    {
                        var batch = documents.Skip(page * batchSize).Take(batchSize).ToArray();
                        storage.Load(_serializer, conn, batch);

                        page++;
                        total += batch.Length;
                    }
                }
            });
        }

        public IDiagnostics Diagnostics { get; }

        public ILoadByKeys<T> Load<T>()
        {
            return new LoadByKeys<T>(this);
        }

        public void SaveChanges()
        {
            _runner.Execute(conn =>
            {
                using (var tx = conn.BeginTransaction())
                {
                    var changes = _documentMap.GetChanges();
                    changes.Each(change =>
                    {
                        using (var command = change.CreateCommand(_schema))
                        {
                            command.Connection = conn;
                            command.Transaction = tx;
                            command.ExecuteNonQuery();
                        }
                    });

                    tx.Commit();

                    _documentMap.ChangesApplied(changes);
                }
            });
        }
        
        private T LoadEntity<T>(object id)
        {
            var entry = _documentMap.Get<T>(id);
            if (entry != null)
            {
                return entry.Document;
            }

            var storage = _schema.StorageFor(typeof(T));

            return _runner.Execute(conn =>
            {
                var loader = storage.LoaderCommand(id);
                loader.Connection = conn;
                var json = loader.ExecuteScalar() as string; // Maybe do this as a stream later for big docs?

                return SerializeLoadedDocument<T>(json);
            });
        }

        private T SerializeLoadedDocument<T>(string json)
        {
            if (json == null) return default(T);

            var document = _serializer.FromJson<T>(json);
            var id = _schema.StorageFor(typeof(T)).Identity(document);

            return _documentMap.Loaded(id, document, json);
        }

        private class LoadByKeys<TDoc> : ILoadByKeys<TDoc>
        {
            private readonly BaseSession _parent;

            public LoadByKeys(BaseSession parent)
            {
                _parent = parent;
            }

            public IEnumerable<TDoc> ById<TKey>(params TKey[] keys)
            {
                var storage = _parent._schema.StorageFor(typeof (TDoc));
                var cmd = storage.LoadByArrayCommand(keys);

                return _parent._runner.QueryJson(cmd)
                    .Select(json => _parent.SerializeLoadedDocument<TDoc>(json));
            }

            public IEnumerable<TDoc> ById<TKey>(IEnumerable<TKey> keys)
            {
                return ById(keys.ToArray());
            }
        }
    }
}