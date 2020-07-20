using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Internal.Storage;
using Marten.Storage;
using Marten.Util;
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

        internal static IWhereFragment BuildWhereFragment(this IDocumentStorage storage, QueryModel query, MartenExpressionParser parser)
        {
            var wheres = query.AllBodyClauses().OfType<WhereClause>().ToArray();
            if (wheres.Length == 0)
                return storage.DefaultWhereFragment();

            var @where = wheres.Length == 1
                ? parser.ParseWhereFragment(storage.Fields, wheres.Single().Predicate)
                : new CompoundWhereFragment(parser, storage.Fields, "and", wheres);

            return storage.FilterDocuments(query, @where);
        }

    }
}
