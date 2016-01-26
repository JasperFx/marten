using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Baseline;
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
        private readonly MartenExpressionParser _parser;

        public DocumentQuery(DocumentMapping mapping, QueryModel query, ISerializer serializer)
        {
            _mapping = mapping;
            _query = query;
            _parser = new MartenExpressionParser(this, serializer);
        }

        public NpgsqlCommand ToAnyCommand()
        {
            var sql = "select (count(*) > 0) as result from " + _mapping.TableName + " as d";

            var command = new NpgsqlCommand();

            var where = buildWhereClause();

            sql = appendLateralJoin(sql);

            if (@where != null) sql += " where " + @where.ToSql(command);

            command.CommandText = sql;

            return command;
        }

        public NpgsqlCommand ToCountCommand()
        {
            var sql = "select count(*) as number from " + _mapping.TableName + " as d";

            var command = new NpgsqlCommand();
            var where = buildWhereClause();

            sql = appendLateralJoin(sql);
            if (@where != null) sql += " where " + @where.ToSql(command);


            command.CommandText = sql;

            return command;
        }


        public NpgsqlCommand ToCommand()
        {
            if (_query.ResultOperators.OfType<LastResultOperator>().Any())
            {
                throw new InvalidOperationException("Marten does not support the Last() or LastOrDefault() operations. Use a combination of ordering and First/FirstOrDefault() instead");
            }

            var command = new NpgsqlCommand();
            var sql = "select d.data from " + _mapping.TableName + " as d";

            var where = buildWhereClause();
            var orderBy = toOrderClause();

            sql = appendLateralJoin(sql);
            if (@where != null) sql += " where " + @where.ToSql(command);

            if (orderBy.IsNotEmpty()) sql += orderBy;

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

            string limitNumber = null;
            if (take != null)
            {
                limitNumber = take.Count.ToString();
            }
            else if (_query.ResultOperators.Any(x => x is FirstResultOperator))
            {
                limitNumber = "1";
            }
            // Got to return more than 1 to make it fail if there is more than one in the db
            else if (_query.ResultOperators.Any(x => x is SingleResultOperator))
            {
                limitNumber = "2";
            }

            return limitNumber == null ? sql : sql + " LIMIT " + limitNumber + " ";
        }

        private string toOrderClause()
        {
            var orders = _query.BodyClauses.OfType<OrderByClause>().SelectMany(x => x.Orderings).ToArray();
            if (!orders.Any()) return string.Empty;

            return " order by " + orders.Select(ToOrderClause).Join(", ");
        }

        public string ToOrderClause(Ordering clause)
        {
            var locator = _parser.JsonLocator(_mapping, clause.Expression);
            return clause.OrderingDirection == OrderingDirection.Asc
                ? locator
                : locator + " desc";
        }

        private IWhereFragment buildWhereClause()
        {
            var wheres = _query.BodyClauses.OfType<WhereClause>().ToArray();
            if (wheres.Length == 0) return null;

            return wheres.Length == 1
                ? _parser.ParseWhereFragment(_mapping, wheres.Single().Predicate)
                : new CompoundWhereFragment(_parser, _mapping, "and", wheres);

        }

        private string appendLateralJoin(string sql)
        {
            var lateralFields =
                _fields.Where(x => x.LateralJoinDeclaration.IsNotEmpty())
                    .Select(x => x.LateralJoinDeclaration)
                    .Distinct()
                    .ToArray();

            if (lateralFields.Any())
            {
                var laterals = lateralFields.Join(", ");
                sql += $", LATERAL jsonb_to_record(d.data) as l({laterals})";
            }
            return sql;
        }


        private readonly IList<IField> _fields = new List<IField>(); 

        public void RegisterField(IField field)
        {
            _fields.Add(field);
        }
    }
}