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

            var command = new NpgsqlCommand();
            sql = appendWhereClause(sql, command);

            command.CommandText = sql;

            return command;
        }

        public NpgsqlCommand ToCommand()
        {
            var command = new NpgsqlCommand();
            var sql = "select data from " + _tableName;

            sql = appendWhereClause(sql, command);

            command.CommandText = sql;

            return command;
        }

        private string appendWhereClause(string sql, NpgsqlCommand command)
        {
            var wheres = _query.BodyClauses.OfType<WhereClause>().ToArray();
            if (wheres.Length == 0) return sql;

            var where = wheres.Length == 1
                ? MartenExpressionParser.ParseWhereFragment(wheres.Single().Predicate)
                : new CompoundWhereFragment("and", wheres);


            sql += " where " + where.ToSql(command);

            return sql;
        }

        public NpgsqlCommand ToCountCommand()
        {
            var sql = "select count(*) as number from " + _tableName;

            var command = new NpgsqlCommand();
            sql = appendWhereClause(sql, command);

            command.CommandText = sql;

            return command;
        }
    }
}