using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        public static void ApplyTake(this QueryModel model, int limit, CommandBuilder sql)
        {
            if (limit > 0)
            {
                sql.Append(" LIMIT ");
                sql.Append(limit);
            }
            else
            {
                var take = model.FindOperators<TakeResultOperator>().LastOrDefault();
                if (take != null)
                {
                    var param = sql.AddParameter(take.Count.Value());
                    sql.Append(" LIMIT :");
                    sql.Append(param.ParameterName);
                }
            }

        }

        public static void ApplySkip(this QueryModel model, CommandBuilder sql)
        {
            var skip = model.FindOperators<SkipResultOperator>().LastOrDefault();
            if (skip != null)
            {
                var param = sql.AddParameter(skip.Count.Value());
                sql.Append(" OFFSET :");
                sql.Append(param.ParameterName);
            }
        }
    }
}