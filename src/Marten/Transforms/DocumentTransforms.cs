using System;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Util;
using Remotion.Linq.Clauses;

namespace Marten.Transforms
{
    public class DocumentTransforms : IDocumentTransforms
    {
        private readonly IDocumentStore _store;

        public DocumentTransforms(IDocumentStore store)
        {
            _store = store;
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
            return $"update {mapping.Table.QualifiedName} as d set data = {transform.Function.QualifiedName}(data), {DocumentMapping.LastModifiedColumn} = (now() at time zone 'utc')";
        }

        public void Where<T>(string transformName, Expression<Func<T, bool>> @where)
        {
            var transform = _store.Schema.TransformFor(transformName);
            var mapping = _store.Schema.MappingFor(typeof(T));
            var sql = toBasicSql(mapping, transform);

            using (var session = _store.LightweightSession())
            {
                var queryModel = session.Query<T>().Where(@where).As<MartenQueryable<T>>().ToQueryModel();

                var wheres = queryModel.BodyClauses.OfType<WhereClause>().ToArray();
                if (wheres.Length == 0)
                {
                    throw new InvalidOperationException();
                }



                var whereFragment = _store.Schema.Parser.ParseWhereFragment(mapping.ToQueryableDocument(), wheres.First().Predicate);
                whereFragment = mapping.ToQueryableDocument().FilterDocuments(queryModel, whereFragment);

                var cmd = session.Connection.CreateCommand();

                sql = sql.AppendWhere(whereFragment, cmd);

                cmd.CommandText = sql;

                cmd.ExecuteNonQuery();

                session.SaveChanges();
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