using System;
using System.Collections.Generic;
using System.Linq;
using FubuCore;
using Marten.Linq;
using Marten.Schema;
using Marten.Util;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    public interface IDocumentExecutor : IQueryExecutor
    {
        NpgsqlCommand BuildCommand<T>(QueryModel queryModel);
    }

    public class DocumentSession : IDocumentSession, IDocumentExecutor
    {
        private readonly IList<NpgsqlCommand> _deletes = new List<NpgsqlCommand>();
        private readonly IConnectionFactory _factory;
        private readonly IQueryParser _parser;
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;

        private readonly IList<object> _updates = new List<object>();

        public DocumentSession(IDocumentSchema schema, ISerializer serializer, IConnectionFactory factory,
            IQueryParser parser)
        {
            _schema = schema;
            _serializer = serializer;
            _factory = factory;
            _parser = parser;
        }

        T IQueryExecutor.ExecuteScalar<T>(QueryModel queryModel)
        {
            if (queryModel.ResultOperators.OfType<AnyResultOperator>().Any())
            {
                var storage = _schema.StorageFor(queryModel.SelectClause.Selector.Type);
                var anyCommand = storage.AnyCommand(queryModel);

                using (var conn = _factory.Create())
                {
                    conn.Open();

                    try
                    {
                        anyCommand.Connection = conn;
                        return (T) anyCommand.ExecuteScalar();
                    }
                    finally
                    {
                        conn.Close();
                    }
                }

            }

            if (queryModel.ResultOperators.OfType<CountResultOperator>().Any())
            {
                var storage = _schema.StorageFor(queryModel.SelectClause.Selector.Type);
                var countCommand = storage.CountCommand(queryModel);

                using (var conn = _factory.Create())
                {
                    conn.Open();

                    try
                    {
                        countCommand.Connection = conn;
                        var returnValue = countCommand.ExecuteScalar();
                        return Convert.ToInt32(returnValue).As<T>();
                    }
                    finally
                    {
                        conn.Close();
                    }
                }
            }

            throw new NotSupportedException();
        }

        T IQueryExecutor.ExecuteSingle<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
        {
            var isFirst = queryModel.ResultOperators.OfType<FirstResultOperator>().Any();

            // TODO -- optimize by returning the count here too?
            var cmd = BuildCommand<T>(queryModel);
            var all = queryJson(cmd).ToArray();

            if (returnDefaultWhenEmpty && all.Length == 0) return default(T);

            var data = isFirst ? all.First() : all.Single();

            return _serializer.FromJson<T>(data);
        }


        IEnumerable<T> IQueryExecutor.ExecuteCollection<T>(QueryModel queryModel)
        {
            var command = BuildCommand<T>(queryModel);

            return query<T>(command);
        }

        public NpgsqlCommand BuildCommand<T>(QueryModel queryModel)
        {
            var tableName = _schema.StorageFor(typeof (T)).TableName;
            var query = new DocumentQuery<T>(tableName);
            var @where = queryModel.BodyClauses.OfType<WhereClause>().FirstOrDefault();
            if (@where != null)
            {
                query.Where = MartenExpressionParser.ParseWhereFragment(@where.Predicate);
            }

            var command = query.ToCommand();
            return command;
        }

        public void Dispose()
        {
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

        private T load<T>(object id)
        {
            var storage = _schema.StorageFor(typeof (T));
            var loader = storage.LoaderCommand(id);

            using (var conn = _factory.Create())
            {
                conn.Open();

                try
                {
                    loader.Connection = conn;
                    var json = loader.ExecuteScalar() as string; // Maybe do this as a stream later for big docs?

                    if (json == null) return default(T);

                    return _serializer.FromJson<T>(json);
                }
                finally
                {
                    conn.Close();
                }
            }
        }


        public void SaveChanges()
        {
            // TODO -- fancier later to add batch updating!

            using (var conn = _factory.Create())
            {
                conn.Open();
                try
                {
                    using (var tx = conn.BeginTransaction())
                    {
                        _updates.Each(o =>
                        {
                            var docType = o.GetType();
                            var storage = _schema.StorageFor(docType);

                            using (var command = storage.UpsertCommand(o, _serializer.ToJson(o)))
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
                        _updates.Clear();
                    }
                }
                finally
                {
                    conn.Close();
                }
            }
        }

        public void Store(object entity)
        {
            // TODO -- throw if null
            _updates.Add(entity);
        }

        public IQueryable<T> Query<T>()
        {
            return new MartenQueryable<T>(_parser, this);
        }

        public IEnumerable<T> Query<T>(string @where, params object[] parameters)
        {
            var tableName = _schema.StorageFor(typeof (T)).TableName;
            var sql = "select data from {0} {1}".ToFormat(tableName, @where);
            var cmd = new NpgsqlCommand();

            parameters.Each(x =>
            {
                var param = cmd.AddParameter(x);
                sql = sql.UseParameter(param);
            });

            cmd.CommandText = sql;

            return query<T>(cmd).ToArray();
        }

        private IEnumerable<T> query<T>(NpgsqlCommand cmd)
        {
            return queryJson(cmd).Select(json => _serializer.FromJson<T>(json));
        }

        private IEnumerable<string> queryJson(NpgsqlCommand cmd)
        {
            using (var conn = _factory.Create())
            {
                conn.Open();
                cmd.Connection = conn;

                try
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            yield return reader.GetString(0);
                        }

                        reader.Close();
                    }
                }
                finally
                {
                    conn.Close();
                }
            }
        }

        public NpgsqlCommand BuildCommand<T>(IQueryable<T> queryable)
        {
            var model = _parser.GetParsedQuery(queryable.Expression);
            return BuildCommand<T>(model);
        }

        public ILoadByKeys<T> Load<T>()
        {
            return new LoadByKeys<T>(this);
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

                return _parent.query<TDoc>(cmd);
            }

            public IEnumerable<TDoc> ById<TKey>(IEnumerable<TKey> keys)
            {
                return ById(keys.ToArray());
            }
        }
    }
}