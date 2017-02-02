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

            var cmd = CommandBuilder.BuildCommand(sql =>
            {
                writeBasicSql(sql, mapping, transform);

                var where = mapping.ToQueryableDocument().DefaultWhereFragment();
                if (where != null)
                {
                    sql.Append(" where ");
                    where.Apply(sql);
                }
            });


            using (var conn = _store.Advanced.OpenConnection())
            {
                conn.Execute(cmd, c =>
                {
                    c.ExecuteNonQuery();
                });
            }
        }

        private static void writeBasicSql(CommandBuilder sql, IDocumentMapping mapping, TransformFunction transform)
        {
            var version = CombGuidIdGeneration.NewGuid();

            sql.Append("update ");
            sql.Append(mapping.Table.QualifiedName);
            sql.Append(" as d set data = ");
            sql.Append(transform.Function.QualifiedName);
            sql.Append("(data), ");
            sql.Append(DocumentMapping.LastModifiedColumn);
            sql.Append(" = (now() at time zone 'utc'), ");
            sql.Append(DocumentMapping.VersionColumn);
            sql.Append(" = '");
            sql.Append(version);
            sql.Append("'");
        }

        public void Where<T>(string transformName, Expression<Func<T, bool>> @where)
        {
            var transform = _store.Schema.TransformFor(transformName);
            var mapping = _store.Schema.MappingFor(typeof(T));
            var whereFragment = findWhereFragment(@where, mapping);

            var cmd = CommandBuilder.BuildCommand(sql =>
            {
                writeBasicSql(sql, mapping, transform);
                if (whereFragment != null)
                {
                    sql.Append(" where ");
                    whereFragment.Apply(sql);
                }
            });



            using (var conn = _factory.Create())
            {
                conn.Open();

                var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);

                cmd.Connection = conn;
                cmd.Transaction = tx;

                cmd.ExecuteNonQuery();

                tx.Commit();
            }
        }

        private IWhereFragment findWhereFragment<T>(Expression<Func<T, bool>> @where, IDocumentMapping mapping)
        {
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
            mapping.ToQueryableDocument().FilterDocuments(queryModel, whereFragment);

            return whereFragment;
        }


        public void Document<T>(string transformName, string id)
        {
            transformOne<T>(transformName, id);
        }

        private void transformOne<T>(string transformName, object id)
        {
            var transform = _store.Schema.TransformFor(transformName);
            var mapping = _store.Schema.MappingFor(typeof(T));

            var cmd = CommandBuilder.BuildCommand(sql =>
            {
                writeBasicSql(sql, mapping, transform);
                sql.Append(" where id = :id");

                sql.AddNamedParameter("id", id);
            });


            using (var conn = _store.Advanced.OpenConnection())
            {
                conn.Execute(cmd, c => c.ExecuteNonQuery());
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