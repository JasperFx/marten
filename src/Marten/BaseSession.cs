using System;
using System.Collections.Generic;
using System.Linq;
using FubuCore;
using Marten.Linq;
using Marten.Map;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    public abstract class BaseSession : ISession
    {
        private readonly IQueryParser _parser;
        private readonly IMartenQueryExecutor _executor;
        private readonly IIdentityMap _documentMap;
        private readonly ICommandRunner _runner;
        private readonly ISerializer _serializer;
        private readonly IDocumentSchema _schema;
        private UnitOfWork _unitOfWork;

        protected BaseSession(IDocumentSchema schema, ISerializer serializer, ICommandRunner runner, IQueryParser parser, IMartenQueryExecutor executor, IIdentityMap documentMap, IDiagnostics diagnostics)
        {
            _schema = schema;
            _serializer = serializer;
            _runner = runner;

            _parser = parser;
            _executor = executor;
            _documentMap = documentMap;
            _unitOfWork = new UnitOfWork(_schema);

            if (_documentMap is IDocumentTracker)
            {
                _unitOfWork.AddTracker(_documentMap.As<IDocumentTracker>());
            }

            Diagnostics = diagnostics;
        }

        public void Dispose()
        {
        }

        public void Delete<T>(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            _unitOfWork.Delete(entity);
            _documentMap.Remove<T>(_schema.StorageFor(typeof(T)).Identity(entity));
        }

        public void Delete<T>(ValueType id)
        {
            _unitOfWork.Delete<T>(id);
            _documentMap.Remove<T>(id);
        }

        public void Delete<T>(string id)
        {
            _unitOfWork.Delete<T>(id);
            _documentMap.Remove<T>(id);
        }

        public T Load<T>(string id) where T : class
        {
            return load<T>(id);
        }

        public T Load<T>(ValueType id) where T : class
        {
            return load<T>(id);
        }

        public void Store<T>(T entity) where T : class
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var storage = _schema.StorageFor(typeof(T));
            var id =storage
                .As<IdAssignment<T>>().Assign(entity);

            if (_documentMap.Has<T>(id))
            {
                var existing = _documentMap.Retrieve<T>(id);
                if (!ReferenceEquals(existing, entity))
                {
                    throw new InvalidOperationException(
                        $"Document '{typeof (T).FullName}' with same Id already added to the session.");
                }
            }
            else
            {
                _unitOfWork.Store(entity);
                _documentMap.Store(id, entity);
            }


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
                    .Select(json => _serializer.FromJson<T>(json))
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

        public ILoadByKeys<T> Load<T>() where T : class
        {
            return new LoadByKeys<T>(this);
        }

        public void SaveChanges()
        {
            var batch = new UpdateBatch(_serializer, _runner);
            _unitOfWork.ApplyChanges(batch);

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


        private class LoadByKeys<TDoc> : ILoadByKeys<TDoc> where TDoc : class
        {
            private readonly BaseSession _parent;

            public LoadByKeys(BaseSession parent)
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
    }
}