using System;
using System.Linq;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;

namespace Marten.Linq
{
    public class DocumentQuery<T>
    {
        private readonly string _tableName;

        public DocumentQuery(string tableName)
        {
            _tableName = tableName;
        }

        public IWhereFragment Where { get; set; }

        public NpgsqlCommand ToAnyCommand(QueryModel query)
        {
            var sql = "select (count(*) > 0) as result from " + _tableName;

            // TODO -- more than one where?
            var where = query.BodyClauses.OfType<WhereClause>().FirstOrDefault();

            var command = new NpgsqlCommand();
            if (where != null)
            {
                sql += " where " + MartenExpressionParser.ParseWhereFragment(where.Predicate).ToSql(command);
            }

            command.CommandText = sql;

            return command;
        }

        // TODO -- make this take in QueryModel
        public NpgsqlCommand ToCommand()
        {
            var command = new NpgsqlCommand();
            var sql = "select data from " + _tableName;

            if (Where != null)
            {
                sql += " where " + Where.ToSql(command);
            }

            command.CommandText = sql;

            return command;
        }

        public NpgsqlCommand ToCountCommand(QueryModel query)
        {
            var sql = "select count(*) as number from " + _tableName;

            // TODO -- more than one where?
            var where = query.BodyClauses.OfType<WhereClause>().FirstOrDefault();

            var command = new NpgsqlCommand();
            if (where != null)
            {
                sql += " where " + MartenExpressionParser.ParseWhereFragment(where.Predicate).ToSql(command);
            }

            command.CommandText = sql;

            return command;
        }
    }
}