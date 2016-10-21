using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Baseline.Conversion;
using Marten.Events;
using Marten.Schema;
using Marten.Transforms;
using Marten.Util;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq
{
    public static class QueryModelExtensions
    {
        private static readonly Conversions conversions = new Conversions();

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

        public static string ToOrderClause(this QueryModel query, IQueryableDocument mapping)
        {
            var orders = query.AllBodyClauses().OfType<OrderByClause>().SelectMany(x => x.Orderings).ToArray();
            if (!orders.Any()) return string.Empty;

            return " order by " + orders.Select(c => ToOrderClause(c, mapping)).Join(", ");
        }

        public static string ToOrderClause(this Ordering clause, IQueryableDocument mapping)
        {
            var locator = mapping.JsonLocator(clause.Expression);
            return clause.OrderingDirection == OrderingDirection.Asc
                ? locator
                : locator + " desc";
        }

        public static IWhereFragment BuildWhereFragment(this IDocumentSchema schema, IQueryableDocument mapping,
            QueryModel query)
        {
            var wheres = query.AllBodyClauses().OfType<WhereClause>().ToArray();
            if (wheres.Length == 0) return mapping.DefaultWhereFragment();

            var where = wheres.Length == 1
                ? schema.Parser.ParseWhereFragment(mapping, wheres.Single().Predicate)
                : new CompoundWhereFragment(schema.Parser, mapping, "and", wheres);

            return mapping.FilterDocuments(query, where);
        }

        public static IWhereFragment BuildWhereFragment(this IDocumentSchema schema, QueryModel query)
        {
            var mapping = schema.MappingFor(query.SourceType()).ToQueryableDocument();
            return schema.BuildWhereFragment(mapping, query);
        }

        public static string AppendOffset(this QueryModel query, string sql)
        {
            var skip = query.FindOperators<SkipResultOperator>().LastOrDefault();

            return skip == null ? sql : sql + " OFFSET " + skip.Count + " ";
        }

        public static string AppendLimit(this QueryModel query, string sql)
        {
            var take = query.FindOperators<TakeResultOperator>().LastOrDefault();

            return take == null ? sql : sql + " LIMIT " + take.Count + " ";
        }

        public static bool HasSelectMany(this QueryModel query)
        {
            return query.SelectClause.Selector is QuerySourceReferenceExpression
                   &&
                   query.SelectClause.Selector.As<QuerySourceReferenceExpression>().ReferencedQuerySource is
                       AdditionalFromClause;
        }

        public static ISelector<T> BuildSelector<T>(this IDocumentSchema schema, IQueryableDocument mapping,
            QueryModel query)
        {
            var selectable = query.AllResultOperators().OfType<ISelectableOperator>().FirstOrDefault();
            if (selectable != null)
                return selectable.BuildSelector<T>(schema, mapping);


            if (query.HasSelectMany())
            {
                return buildSelectorForSelectMany<T>(mapping, query, schema.StoreOptions.Serializer());
            }

            if (query.SelectClause.Selector.Type == query.SourceType())
            {
                if (typeof(T) == typeof(string))
                    return (ISelector<T>) new JsonSelector();

                // I'm so ashamed of this hack, but "simplest thing that works"
                if (typeof(T) == typeof(IEvent))
                    return mapping.As<EventQueryMapping>().Selector.As<ISelector<T>>();

                var resolver = schema.ResolverFor<T>();
                return new WholeDocumentSelector<T>(mapping, resolver);
            }



            return createSelectTransformSelector<T>(schema, mapping, query);
        }

        private static ISelector<T> buildSelectorForSelectMany<T>(IQueryableDocument mapping, QueryModel query, ISerializer serializer)
        {
            var expression = query.SelectClause.Selector.As<QuerySourceReferenceExpression>();
            var from = expression.ReferencedQuerySource.As<AdditionalFromClause>().FromExpression;

            var members = FindMembers.Determine(from);
            var field = mapping.FieldFor(members);

            // What if there's a select here? Worry about that later?

            var isDistinct = query.HasOperator<DistinctResultOperator>();

            if (typeof(T) == typeof(string))
            {
                return new SingleFieldSelector<T>(isDistinct, $"jsonb_array_elements_text({field.SqlLocator})");
            }
            else if (TypeMappings.HasTypeMapping(typeof(T)))
            {
                return new ArrayElementFieldSelector<T>(isDistinct, field, conversions);
            }

            return new DeserializeSelector<T>(serializer, $"jsonb_array_elements_text({field.SqlLocator})");
            
        }

        private static ISelector<T> createSelectTransformSelector<T>(IDocumentSchema schema, IQueryableDocument mapping,
            QueryModel query)
        {
            var visitor = new SelectorParser(query);
            visitor.Visit(query.SelectClause.Selector);

            return visitor.ToSelector<T>(schema, mapping);
        }

        public static ISelector<T> BuildSelector<T>(this IDocumentSchema schema, QueryModel query)
        {
            var mapping = schema.MappingFor(query.SourceType()).ToQueryableDocument();
            return schema.BuildSelector<T>(mapping, query);
        }

        public static IDocumentMapping MappingFor(this IDocumentSchema schema, QueryModel model)
        {
            return schema.MappingFor(model.SourceType());
        }
    }
}