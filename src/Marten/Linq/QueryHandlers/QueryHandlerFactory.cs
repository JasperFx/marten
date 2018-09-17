using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq.Compiled;
using Marten.Linq.Model;
using Marten.Linq.QueryHandlers.CompiledInclude;
using Marten.Schema;
using Marten.Services.Includes;
using Marten.Util;
using Remotion.Linq;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq.QueryHandlers
{
    public interface IQueryHandlerFactory
    {
        IQueryHandler<T> HandlerForScalarQuery<T>(QueryModel model);

        IQueryHandler<T> HandlerForScalarQuery<T>(QueryModel model, IIncludeJoin[] toArray, QueryStatistics statistics);

        IQueryHandler<T> HandlerForSingleQuery<T>(QueryModel model, IIncludeJoin[] joins, bool returnDefaultWhenEmpty);

        IQueryHandler<T> HandlerForSingleQuery<T>(QueryModel model, IIncludeJoin[] joins, QueryStatistics statistics,
            bool returnDefaultWhenEmpty);

        IQueryHandler<T> BuildHandler<T>(QueryModel model, IIncludeJoin[] joins, QueryStatistics stats);

        IQueryHandler<TOut> HandlerFor<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, out QueryStatistics stats);
    }

    public class QueryHandlerFactory : IQueryHandlerFactory
    {
        private readonly DocumentStore _store;
        private readonly ConcurrentCache<Type, CachedQuery> _cache = new ConcurrentCache<Type, CachedQuery>();

        public QueryHandlerFactory(DocumentStore store)
        {
            _store = store;
        }

        public IQueryHandler<T> BuildHandler<T>(QueryModel model, IIncludeJoin[] joins, QueryStatistics stats)
        {
            return tryFindScalarQuery<T>(model, joins, stats) ??
                   tryFindSingleQuery<T>(model, joins, stats) ?? listHandlerFor<T>(model, joins, stats);
        }

        public IQueryHandler<T> HandlerForScalarQuery<T>(QueryModel model)
        {
            return HandlerForScalarQuery<T>(model, new IIncludeJoin[0], null);
        }

        // TODO -- going to have to do the ensure storage exists outside of this
        public IQueryHandler<T> HandlerForScalarQuery<T>(QueryModel model, IIncludeJoin[] joins,
            QueryStatistics statistics)
        {
            _store.Tenancy.Default.EnsureStorageExists(model.SourceType());

            return tryFindScalarQuery<T>(model, joins, statistics);
        }

        // TODO -- going to have to do the ensure storage exists outside of this
        public IQueryHandler<T> HandlerForSingleQuery<T>(QueryModel model, IIncludeJoin[] joins,
            QueryStatistics statistics,
            bool returnDefaultWhenEmpty)
        {
            _store.Tenancy.Default.EnsureStorageExists(model.SourceType());

            return tryFindSingleQuery<T>(model, joins, statistics);
        }

        public IQueryHandler<T> HandlerForSingleQuery<T>(QueryModel model, IIncludeJoin[] joins,
            bool returnDefaultWhenEmpty)
        {
            return HandlerForSingleQuery<T>(model, joins, null, returnDefaultWhenEmpty);
        }

        public IQueryHandler<TOut> HandlerFor<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, out QueryStatistics stats)
        {
            var queryType = query.GetType();
            CachedQuery cachedQuery;
            if (!_cache.Has(queryType))
            {
                cachedQuery = buildCachedQuery(queryType, query);

                _cache[queryType] = cachedQuery;
            }
            else
            {
                cachedQuery = _cache[queryType];
            }

            return cachedQuery.CreateHandler<TOut>(query, _store.Serializer, out stats);
        }

        private IQueryHandler<T> listHandlerFor<T>(QueryModel model, IIncludeJoin[] joins, QueryStatistics stats)
        {
            if (model.HasOperator<ToJsonArrayResultOperator>())
            {
                var query = new LinqQuery<T>(_store, model, joins, stats);
                return new JsonQueryHandler(query.As<LinqQuery<string>>()).As<IQueryHandler<T>>();
            }

            if (!typeof(T).IsGenericEnumerable())
                return null;

            var elementType = typeof(T).GetGenericArguments().First();
            var handlerType = typeof(LinqQuery<>);

            if (typeof(T).GetGenericTypeDefinition() == typeof(IEnumerable<>))
                handlerType = typeof(EnumerableQueryHandler<>);

            // TODO -- WTH?
            return
                Activator.CreateInstance(handlerType.MakeGenericType(elementType), _store, model, joins, stats)
                    .As<IQueryHandler<T>>();
        }

        private IQueryHandler<T> tryFindScalarQuery<T>(QueryModel model, IIncludeJoin[] joins, QueryStatistics stats)
        {
            if (model.HasOperator<CountResultOperator>() || model.HasOperator<LongCountResultOperator>())
                return new LinqQuery<T>(_store, model, joins, stats).ToCount<T>();

            if (model.HasOperator<SumResultOperator>())
                return AggregateQueryHandler<T>.Sum(new LinqQuery<T>(_store, model, joins, stats));

            if (model.HasOperator<AverageResultOperator>())
                return AggregateQueryHandler<T>.Average(new LinqQuery<T>(_store, model, joins, stats));

            if (model.HasOperator<AnyResultOperator>())
                return new LinqQuery<T>(_store, model, joins, stats).ToAny().As<IQueryHandler<T>>();

            return null;
        }

        private IQueryHandler<T> tryFindSingleQuery<T>(QueryModel model, IIncludeJoin[] joins, QueryStatistics stats)
        {
            var choice = model.FindOperators<ChoiceResultOperatorBase>().FirstOrDefault();

            if (choice == null) return null;

            var query = new LinqQuery<T>(_store, model, joins, stats);

            if (choice is FirstResultOperator)
            {
                return choice.ReturnDefaultWhenEmpty
                    ? OneResultHandler<T>.FirstOrDefault(query)
                    : OneResultHandler<T>.First(query);
            }

            if (choice is SingleResultOperator)
            {
                return choice.ReturnDefaultWhenEmpty
                    ? OneResultHandler<T>.SingleOrDefault(query)
                    : OneResultHandler<T>.Single(query);
            }

            if (choice is MinResultOperator)
            {
                return AggregateQueryHandler<T>.Min(query);
            }

            if (choice is MaxResultOperator)
            {
                return AggregateQueryHandler<T>.Max(query);
            }

            if (model.HasOperator<LastResultOperator>())
            {
                throw new InvalidOperationException(
                    "Marten does not support Last()/LastOrDefault(). Use reverse ordering and First()/FirstOrDefault() instead");
            }
            return null;
        }

        private CachedQuery buildCachedQuery<TDoc, TOut>(Type queryType, ICompiledQuery<TDoc, TOut> query)
        {
            Expression expression = query.QueryIs();
            var invocation = Expression.Invoke(expression, Expression.Parameter(typeof(IMartenQueryable<TDoc>)));

            var queryableDocument = _store.Tenancy.Default.MappingFor(typeof(TDoc)).ToQueryableDocument();

            var setters = findSetters(queryableDocument, queryType, expression, _store.Serializer);

            var model = MartenQueryParser.TransformQueryFlyweight.GetParsedQuery(invocation);

            validateCompiledQuery(model);

            // TODO -- move this outside of this call
            _store.Tenancy.Default.EnsureStorageExists(typeof(TDoc));

            var includeJoins = new IIncludeJoin[0];

            if (model.HasOperator<IncludeResultOperator>())
            {
                var builder = new CompiledIncludeJoinBuilder<TDoc, TOut>(_store.Storage);
                includeJoins = builder.BuildIncludeJoins(model, query);
            }

            // Hokey. Need a non-null stats to trigger LinqQuery into "knowing" that it needs
            // to create a StatsSelector decorator
            var stats = model.HasOperator<StatsResultOperator>() ? new QueryStatistics() : null;

            var handler = _store.HandlerFactory.BuildHandler<TOut>(model, includeJoins, stats);

            var cmd = CommandBuilder.ToCommand(_store.Tenancy.Default, handler);
            for (int i = 0; i < setters.Count && i < cmd.Parameters.Count; i++)
            {
                setters[i].ReplaceValue(cmd.Parameters[i]);
            }

            var cachedQuery = new CachedQuery
            {
                Command = cmd,
                ParameterSetters = setters,
                Handler = handler,
            };

            if (model.HasOperator<StatsResultOperator>())
            {
                var prop = queryType.GetProperties().FirstOrDefault(x => x.PropertyType == typeof(QueryStatistics));
                if (prop != null)
                {
                    cachedQuery.StatisticsFinder =
                        typeof(QueryStatisticsFinder<>).CloseAndBuildAs<IQueryStatisticsFinder>(prop, queryType);
                }
            }

            return cachedQuery;
        }

        private static void validateCompiledQuery(QueryModel model)
        {
            var skip = model.FindOperators<SkipResultOperator>().LastOrDefault();
            var take = model.FindOperators<TakeResultOperator>().LastOrDefault();

            if (skip != null && take != null)
            {
                var skipIndex = model.ResultOperators.IndexOf(skip);
                var takeIndex = model.ResultOperators.IndexOf(take);

                if (skipIndex > takeIndex)
                {
                    throw new InvalidCompiledQueryException("Skip() must precede Take() in compiled queries for proper parameter resolution");
                }
            }
        }

        private static IList<IDbParameterSetter> findSetters(IQueryableDocument mapping, Type queryType, Expression expression, ISerializer serializer)
        {
            var visitor = new CompiledQueryMemberExpressionVisitor(mapping, queryType, serializer);
            visitor.Visit(expression);

            var parameterSetters = visitor.ParameterSetters;

            return parameterSetters;
        }
    }
}