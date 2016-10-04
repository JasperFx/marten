using System;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Util;
using Remotion.Linq;
using Remotion.Linq.Clauses;

namespace Marten.Transforms
{
    public class DocumentTransforms : IDocumentTransforms
    {
        private readonly IDocumentStore _store;
        private readonly IConnectionFactory _factory;

        public DocumentTransforms(IDocumentStore store, IConnectionFactory factory)
        {
            _store = store;
            _factory = factory;
        }

        public void All<T>(string transformName)
        {
            var transform = _store.Schema.TransformFor(transformName);
            var mapping = _store.Schema.MappingFor(typeof(T));


            var sql = toBasicSql(mapping, transform);
            var where = mapping.ToQueryableDocument().DefaultWhereFragment();

            using (var conn = _store.Advanced.OpenConnection())
            {
                conn.Execute(c =>
                {
                    if (where != null)
                    {
                        sql = sql.AppendWhere(where, c);
                    }

                    c.Sql(sql);
                    c.ExecuteNonQuery();
                });
            }
        }

        private static string toBasicSql(IDocumentMapping mapping, TransformFunction transform)
        {
            var version = CombGuidIdGeneration.NewGuid();
            return $"update {mapping.Table.QualifiedName} as d set data = {transform.Function.QualifiedName}(data), {DocumentMapping.LastModifiedColumn} = (now() at time zone 'utc'), {DocumentMapping.VersionColumn} = '{version}'";
        }

        public void Where<T>(string transformName, Expression<Func<T, bool>> @where)
        {
            var transform = _store.Schema.TransformFor(transformName);
            var mapping = _store.Schema.MappingFor(typeof(T));
            var sql = toBasicSql(mapping, transform);

            QueryModel queryModel;
            using (var session = _store.QuerySession())
            {
                queryModel = session.Query<T>().Where(@where).As<MartenQueryable<T>>().ToQueryModel(); 
            }

            var wheres = queryModel.BodyClauses.OfType<WhereClause>().ToArray();
            if (wheres.Length == 0)
            {
                throw new InvalidOperationException();
            }

            var whereFragment = _store.Schema.Parser.ParseWhereFragment(mapping.ToQueryableDocument(), wheres.First().Predicate);
            whereFragment = mapping.ToQueryableDocument().FilterDocuments(queryModel, whereFragment);

            using (var conn = _factory.Create())
            {
                var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);
                var cmd = conn.CreateCommand();
                cmd.Transaction = tx;

                sql = sql.AppendWhere(whereFragment, cmd);

                cmd.CommandText = sql;

                cmd.ExecuteNonQuery();

            }
        }


        public void Document<T>(string transformName, string id)
        {
            transformOne<T>(transformName, id);
        }

        private void transformOne<T>(string transformName, object id)
        {
            var transform = _store.Schema.TransformFor(transformName);
            var mapping = _store.Schema.MappingFor(typeof(T));


            var sql = toBasicSql(mapping, transform) + " where id = :id";

            using (var conn = _store.Advanced.OpenConnection())
            {
                conn.Execute(c => c.Sql(sql).With("id", id).ExecuteNonQuery());
            }
        }

        public void Document<T>(string transformName, int id)
        {
            transformOne<T>(transformName, id);
        }

        public void Document<T>(string transformName, long id)
        {
            transformOne<T>(transformName, id);
        }

        public void Document<T>(string transformName, Guid id)
        {
            transformOne<T>(transformName, id);
        }
    }
}