using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using FubuCore;
using Marten.Linq;
using Marten.Schema;
using Marten.Util;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;
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
        private readonly QueryParser _queryParser;
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;

        private readonly IList<object> _updates = new List<object>();

        public DocumentSession(IDocumentSchema schema, ISerializer serializer, IConnectionFactory factory, IQueryParser parser)
        {
            _schema = schema;
            _serializer = serializer;
            _factory = factory;
            _parser = parser;
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
            throw new NotImplementedException();
        }

        public T[] Load<T>(IEnumerable<string> ids)
        {
            throw new NotImplementedException();
        }

        public T Load<T>(ValueType id)
        {
            var storage = _schema.StorageFor(typeof (T));
            var loader = storage.LoaderCommand(id);

            using (var conn = _factory.Create())
            {
                conn.Open();

                loader.Connection = conn;
                var json = loader.ExecuteScalar() as string; // Maybe do this as a stream later for big docs?

                if (json == null) return default(T);

                return _serializer.FromJson<T>(json);
            }
        }

        public T[] Load<T>(params ValueType[] ids)
        {
            throw new NotImplementedException();
        }

        public T[] Load<T>(IEnumerable<ValueType> ids)
        {
            throw new NotImplementedException();
        }

        public void SaveChanges()
        {
            // TODO -- fancier later to add batch updating!

            using (var conn = _factory.Create())
            {
                conn.Open();
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
            using (var conn = _factory.Create())
            {
                conn.Open();
                cmd.Connection = conn;

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var json = reader.GetString(0);
                        yield return _serializer.FromJson<T>(json);
                    }
                }
            }
        }

        T IQueryExecutor.ExecuteScalar<T>(QueryModel queryModel)
        {
            throw new NotImplementedException();
        }

        T IQueryExecutor.ExecuteSingle<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
        {
            throw new NotImplementedException();
        }

        IEnumerable<T> IQueryExecutor.ExecuteCollection<T>(QueryModel queryModel)
        {
            var command = BuildCommand<T>(queryModel);

            return query<T>(command);
        }

        public NpgsqlCommand BuildCommand<T>(QueryModel queryModel)
        {
            var query = new DocumentQuery<T>(_schema.StorageFor(typeof (T)).TableName);
            var @where = queryModel.BodyClauses.OfType<WhereClause>().FirstOrDefault();
            if (@where != null)
            {
                query.Where = MartenExpressionParser.ParseWhereFragment(@where.Predicate);
            }

            var command = query.ToCommand();
            return command;
        }
    }
}