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
        private readonly QueryModel _query;

        public DocumentQuery(string tableName, QueryModel query)
        {
            _tableName = tableName;
            _query = query;
        }

        public NpgsqlCommand ToAnyCommand()
        {
            var sql = "select (count(*) > 0) as result from " + _tableName;

            // TODO -- more than one where?
            var where = _query.BodyClauses.OfType<WhereClause>().FirstOrDefault();

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

            var where = _query.BodyClauses.OfType<WhereClause>().FirstOrDefault();
            if (where != null)
            {
                sql += " where " + MartenExpressionParser.ParseWhereFragment(where.Predicate).ToSql(command);
            }

            command.CommandText = sql;

            return command;
        }

        public NpgsqlCommand ToCountCommand()
        {
            var sql = "select count(*) as number from " + _tableName;

            // TODO -- more than one where?
            var where = _query.BodyClauses.OfType<WhereClause>().FirstOrDefault();

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