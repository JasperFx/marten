using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using FubuCore;
using Marten.Linq;
using Marten.Map;
using Marten.Schema;
using Marten.Util;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    public interface IMartenQueryExecutor : IQueryExecutor
    {
        NpgsqlCommand BuildCommand<T>(QueryModel queryModel);
        NpgsqlCommand BuildCommand<T>(IQueryable<T> queryable);
    }

    public class DocumentSession : IDocumentSession, IDiagnostics
    {
        private readonly IDocumentMap _documentMap;
        private readonly IList<NpgsqlCommand> _deletes = new List<NpgsqlCommand>();
        private readonly IQueryParser _parser;
        private readonly IMartenQueryExecutor _executor;
        private readonly ICommandRunner _runner;
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;

        public DocumentSession(IDocumentSchema schema, ISerializer serializer, ICommandRunner runner, IQueryParser parser, IMartenQueryExecutor executor)
        {
            _schema = schema;
            _serializer = serializer;
            _parser = parser;
            _executor = executor;
            _runner = runner;
            _documentMap = new DocumentMap(_serializer);
        }

        public void Dispose()
        {
        }

        public IDbCommand CommandFor<T>(IQueryable<T> queryable)
        {
            if (queryable is MartenQueryable<T>)
            {
                return _executor.BuildCommand<T>(queryable);
            }

            throw new ArgumentOutOfRangeException(nameof(queryable), "This mechanism can only be used for MartenQueryable<T> objects");
        }

        public void Delete<T>(T entity)
        {
            var storage = _schema.StorageFor(typeof (T));
            _deletes.Add(storage.DeleteCommandForEntity(entity));
        }

        public void Delete<T>(ValueType id)
        {
            var storage = _schema.StorageFor(typeof (T));
            _deletes.Add(storage.DeleteCommandForId(id));
        }

        public void Delete<T>(string id)
        {
            var storage = _schema.StorageFor(typeof (T));
            _deletes.Add(storage.DeleteCommandForId(id));
        }

        public T Load<T>(string id)
        {
            return load<T>(id);
        }

        public T Load<T>(ValueType id)
        {
            return load<T>(id);
        }


        public void SaveChanges()
        {
            // TODO -- fancier later to add batch updating!

            _runner.Execute(conn =>
            {
                using (var tx = conn.BeginTransaction())
                {
                    var updates = _documentMap.GetUpdates();
                    updates.Each(o =>
                    {
                        var docType = o.Document.GetType();
                        var storage = _schema.StorageFor(docType);

                        using (var command = storage.UpsertCommand(o.Document, o.Json))
                        {
                            command.Connection = conn;
                            command.Transaction = tx;

                            command.ExecuteNonQuery();
                        }
                    });

                    _deletes.Each(cmd =>
                    {
                        cmd.Connection = conn;
                        cmd.Transaction = tx;
                        cmd.ExecuteNonQuery();
                    });

                    tx.Commit();

                    _deletes.Clear();
                    _documentMap.Updated(updates);
                }
            });
        }

        public void Store<T>(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var id = _schema.StorageFor(typeof(T))
				.As<IdAssignment<T>>().Assign(entity);
				
            _documentMap.Set<T>(id, entity);
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


            var cmd = new NpgsqlCommand();

            parameters.Each(x =>
            {
                var param = cmd.AddParameter(x);
                sql = sql.UseParameter(param);
            });

            cmd.CommandText = sql;

            var idRetriever = _schema.StorageFor(typeof(T)).As<IdRetriever<T>>();

            return _runner.QueryJson(cmd)
                .Select(json => fromMapOrSerialize(idRetriever, json))
                .ToArray();
        }

        public void BulkInsert<T>(T[] documents, int batchSize = 1000)
        {
            var storage = _schema.StorageFor(typeof (T)).As<IBulkLoader<T>>();

            _runner.Execute(conn =>
            {
                var tx = conn.BeginTransaction();
                try
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

                    

                    tx.Commit();
                }
                catch (Exception)
                {
                    tx.Rollback();
                }
            });

        }

        public IDiagnostics Diagnostics
        {
            get { return this; }
        }

        public ILoadByKeys<T> Load<T>()
        {
            return new LoadByKeys<T>(this);
        }

        private T load<T>(object id)
        {
            var entry = _documentMap.Get<T>(id);
            if (entry != null)
            {
                return entry.Document;
            }

            var storage = _schema.StorageFor(typeof (T));
            var idRetriever = storage.As<IdRetriever<T>>();

            return _runner.Execute(conn =>
            {
                var loader = storage.LoaderCommand(id);
                loader.Connection = conn;
                var json = loader.ExecuteScalar() as string; // Maybe do this as a stream later for big docs?

                return fromMapOrSerialize(idRetriever, json);
            });
        }

        private T fromMapOrSerialize<T>(IdRetriever<T> idRetriever, string json)
        {
            if (json == null) return default(T);

            var document = _serializer.FromJson<T>(json);
            var id = idRetriever.Retrieve(document);

            var mapEntry = _documentMap.Get<T>(id);
            if (mapEntry != null)
            {
                return mapEntry.Document;
            }

            _documentMap.Set(id, document, json);
            return document;
        }

        public class LoadByKeys<TDoc> : ILoadByKeys<TDoc>
        {
            private readonly DocumentSession _parent;

            public LoadByKeys(DocumentSession parent)
            {
                _parent = parent;
            }

            public IEnumerable<TDoc> ById<TKey>(params TKey[] keys)
            {
                var storage = _parent._schema.StorageFor(typeof (TDoc));
                var cmd = storage.LoadByArrayCommand(keys);

                var idRetriever = storage.As<IdRetriever<TDoc>>();

                return _parent._runner.QueryJson(cmd)
                    .Select(json => _parent.fromMapOrSerialize(idRetriever, json));
            }

            public IEnumerable<TDoc> ById<TKey>(IEnumerable<TKey> keys)
            {
                return ById(keys.ToArray());
            }
        }
    }
}