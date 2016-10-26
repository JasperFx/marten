using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Util;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq
{
    public static class QueryModelExtensions
    {
        public static Type SourceType(this QueryModel query)
        {
            return query.MainFromClause.ItemType;
        }

        public static IEnumerable<ResultOperatorBase> AllResultOperators(this QueryModel query)
        {
            foreach (var @operator in query.ResultOperators)
                yield return @operator;

            if (query.MainFromClause.FromExpression is SubQueryExpression)
                foreach (
                    var @operator in
                    query.MainFromClause.FromExpression.As<SubQueryExpression>().QueryModel.ResultOperators)
                    yield return @operator;
        }

        public static IEnumerable<IBodyClause> AllBodyClauses(this QueryModel query)
        {
            foreach (var clause in query.BodyClauses)
                yield return clause;

            if (query.MainFromClause.FromExpression is SubQueryExpression)
                foreach (
                    var clause in query.MainFromClause.FromExpression.As<SubQueryExpression>().QueryModel.BodyClauses)
                    yield return clause;
        }

        public static IEnumerable<T> FindOperators<T>(this QueryModel query) where T : ResultOperatorBase
        {
            return query.AllResultOperators().OfType<T>();
        }

        public static bool HasOperator<T>(this QueryModel query) where T : ResultOperatorBase
        {
            return query.AllResultOperators().Any(x => x is T);
        }

        public static IWhereFragment BuildWhereFragment(this IDocumentSchema schema, QueryModel query)
        {
            var mapping = schema.MappingFor(query.SourceType()).ToQueryableDocument();
            var wheres = query.AllBodyClauses().OfType<WhereClause>().ToArray();
            if (wheres.Length == 0) return mapping.DefaultWhereFragment();

            var @where = wheres.Length == 1
                ? schema.Parser.ParseWhereFragment(mapping, wheres.Single().Predicate)
                : new CompoundWhereFragment(schema.Parser, mapping, "and", wheres);

            return mapping.FilterDocuments(query, @where);
        }

        public static string ApplyTake(this QueryModel model, NpgsqlCommand command, int limit, string sql)
        {
            if (limit > 0)
            {
                sql += " LIMIT " + limit;
            }
            else
            {
                var take = model.FindOperators<TakeResultOperator>().LastOrDefault();
                if (take != null)
                {
                    var param = command.AddParameter(take.Count.Value());
                    sql += " LIMIT :" + param.ParameterName;
                }
            }

            return sql;
        }

        public static string ApplySkip(this QueryModel model, NpgsqlCommand command, string sql)
        {
            var skip = model.FindOperators<SkipResultOperator>().LastOrDefault();
            if (skip != null)
            {
                var param = command.AddParameter(skip.Count.Value());
                sql += " OFFSET :" + param.ParameterName;
            }
            return sql;
        }
    }
}