using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Marten.Schema;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq
{
    public class DocumentQuery
    {
        private readonly DocumentMapping _mapping;
        private readonly QueryModel _query;

        public DocumentQuery(DocumentMapping mapping, QueryModel query)
        {
            _mapping = mapping;
            _query = query;
        }

        public NpgsqlCommand ToAnyCommand()
        {
            var sql = "select (count(*) > 0) as result from " + _mapping.TableName;

            var command = new NpgsqlCommand();
            sql = appendWhereClause(sql, command);

            command.CommandText = sql;

            return command;
        }

        public NpgsqlCommand ToCommand()
        {
            var command = new NpgsqlCommand();
            var sql = "select data from " + _mapping.TableName;

            sql = appendWhereClause(sql, command);
            sql = appendOrderClause(sql);

            sql = appendLimit(sql);
            sql = appendOffset(sql);

            command.CommandText = sql;

            return command;
        }

        private string appendOffset(string sql)
        {
            var take =
                _query.ResultOperators.OfType<SkipResultOperator>().OrderByDescending(x => x.Count).FirstOrDefault();

            return take == null ? sql : sql + " OFFSET " + take.Count + " ";
        }

        private string appendLimit(string sql)
        {
            var take =
                _query.ResultOperators.OfType<TakeResultOperator>().OrderByDescending(x => x.Count).FirstOrDefault();

            return take == null ? sql : sql + " LIMIT " + take.Count + " ";
        }

        private string appendOrderClause(string sql)
        {
            var orders = _query.BodyClauses.OfType<OrderByClause>().SelectMany(x => x.Orderings).ToArray();
            if (!orders.Any()) return sql;

            return sql += " order by " + orders.Select(ToOrderClause).Join(", ");
        }

        public string ToOrderClause(Ordering clause)
        {
            var locator = MartenExpressionParser.JsonLocator(_mapping, clause.Expression);
            return clause.OrderingDirection == OrderingDirection.Asc
                ? locator
                : locator + " desc";
        }

        private string appendWhereClause(string sql, NpgsqlCommand command)
        {
            var wheres = _query.BodyClauses.OfType<WhereClause>().ToArray();
            if (wheres.Length == 0) return sql;

            var where = wheres.Length == 1
                ? MartenExpressionParser.ParseWhereFragment(_mapping, wheres.Single().Predicate)
                : new CompoundWhereFragment(_mapping, "and", wheres);


            sql += " where " + where.ToSql(command);

            return sql;
        }

        public NpgsqlCommand ToCountCommand()
        {
            var sql = "select count(*) as number from " + _mapping.TableName;

            var command = new NpgsqlCommand();
            sql = appendWhereClause(sql, command);

            command.CommandText = sql;

            return command;
        }
    }
}