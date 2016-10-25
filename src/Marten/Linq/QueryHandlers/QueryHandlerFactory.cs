using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Events;
using Marten.Linq.Compiled;
using Marten.Linq.Model;
using Marten.Linq.QueryHandlers.CompiledInclude;
using Marten.Schema;
using Marten.Services.Includes;
using Marten.Transforms;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq.QueryHandlers
{
    public interface IQueryHandlerFactory
    {
        IQueryHandler<T> HandlerForScalarQuery<T>(QueryModel model);
        IQueryHandler<T> HandlerForScalarQuery<T>(QueryModel model, IIncludeJoin[] toArray, QueryStatistics statistics);


        IQueryHandler<T> HandlerForSingleQuery<T>(QueryModel model, IIncludeJoin[] joins, bool returnDefaultWhenEmpty);

        IQueryHandler<T> HandlerForSingleQuery<T>(QueryModel model, IIncludeJoin[] joins, QueryStatistics statistics, bool returnDefaultWhenEmpty);

        IQueryHandler<T> BuildHandler<T>(QueryModel model, IIncludeJoin[] joins, QueryStatistics stats);

        IQueryHandler<TOut> HandlerFor<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query);

        ISelector<T> BuildSelector<T>(QueryModel query, IIncludeJoin[] joins, QueryStatistics stats);
        
    }

    public class QueryHandlerFactory : IQueryHandlerFactory
    {
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;
        private readonly ConcurrentCache<Type, CachedQuery> _cache = new ConcurrentCache<Type, CachedQuery>();

        public QueryStatistics Stats { get; set; }

        public QueryHandlerFactory(IDocumentSchema schema, ISerializer serializer)
        {
            _schema = schema;
            _serializer = serializer;
        }

        public IQueryHandler<T> BuildHandler<T>(QueryModel model, IIncludeJoin[] joins, QueryStatistics stats)
        {
            return tryFindScalarQuery<T>(model, joins, stats) ?? tryFindSingleQuery<T>(model, joins, stats) ?? listHandlerFor<T>(model, joins, stats);
        }

        private IQueryHandler<T> listHandlerFor<T>(QueryModel model, IIncludeJoin[] joins, QueryStatistics stats)
        {
            if (model.HasOperator<ToJsonArrayResultOperator>())
            {
                var query = new LinqQuery<T>(_schema, model, joins, stats);
                return new JsonQueryHandler(query.As<LinqQuery<string>>()).As<IQueryHandler<T>>();
            }

            if (!typeof (T).IsGenericEnumerable())
            {
                return null;
            }

            var elementType = typeof (T).GetGenericArguments().First();
            var handlerType = typeof(LinqQuery<>);

            if (typeof (T).GetGenericTypeDefinition() == typeof (IEnumerable<>))
            {
                handlerType = typeof (EnumerableQueryHandler<>);
            }

            // TODO -- WTH?
            return Activator.CreateInstance(handlerType.MakeGenericType(elementType), new object[] { _schema, model, joins, stats}).As<IQueryHandler<T>>();
        }

        public IQueryHandler<T> HandlerForScalarQuery<T>(QueryModel model)
        {
            return HandlerForScalarQuery<T>(model, new IIncludeJoin[0], null);
        }

        public IQueryHandler<T> HandlerForScalarQuery<T>(QueryModel model, IIncludeJoin[] joins, QueryStatistics statistics)
        {
            _schema.EnsureStorageExists(model.SourceType());

            return tryFindScalarQuery<T>(model, joins, statistics);
        }

        private IQueryHandler<T> tryFindScalarQuery<T>(QueryModel model, IIncludeJoin[] joins, QueryStatistics stats)
        {
            if (model.HasOperator<CountResultOperator>() || model.HasOperator<LongCountResultOperator>())
            {
                return new LinqQuery<T>(_schema, model, joins, stats).ToCount<T>();
            }

            if (model.HasOperator<SumResultOperator>())
            {
                return AggregateQueryHandler<T>.Sum(_schema, model);
            }

            if (model.HasOperator<AverageResultOperator>())
            {
                return AggregateQueryHandler<T>.Average(_schema, model);
            }

            if (model.HasOperator<AnyResultOperator>())
            {
                return new AnyQueryHandler(model, _schema).As<IQueryHandler<T>>();
            }

            return null;
        }

        public IQueryHandler<T> HandlerForSingleQuery<T>(QueryModel model, IIncludeJoin[] joins, QueryStatistics statistics,
            bool returnDefaultWhenEmpty)
        {
            _schema.EnsureStorageExists(model.SourceType());

            return tryFindSingleQuery<T>(model, joins, statistics);
        }

        public IQueryHandler<T> HandlerForSingleQuery<T>(QueryModel model, IIncludeJoin[] joins, bool returnDefaultWhenEmpty)
        {
            return HandlerForSingleQuery<T>(model, joins, null, returnDefaultWhenEmpty);
        }

        private IQueryHandler<T> tryFindSingleQuery<T>(QueryModel model, IIncludeJoin[] joins, QueryStatistics stats)
        {
            var choice = model.FindOperators<ChoiceResultOperatorBase>().FirstOrDefault();

            if (choice == null) return null;

            var query = new LinqQuery<T>(_schema, model, joins, stats);

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
                return AggregateQueryHandler<T>.Min(_schema, model);
            }

            if (choice is MaxResultOperator)
            {
                return AggregateQueryHandler<T>.Max(_schema, model);
            }

            if (model.HasOperator<LastResultOperator>())
            {
                throw new InvalidOperationException(
                    "Marten does not support Last()/LastOrDefault(). Use reverse ordering and First()/FirstOrDefault() instead");
            }

            return null;
        }

        public IQueryHandler<TOut> HandlerFor<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query)
        {
            var queryType = query.GetType();
            CachedQuery cachedQuery;
            if (!_cache.Has(queryType))
            {
                cachedQuery = buildCachedQuery<TDoc, TOut>(queryType, query);

                _cache[queryType] = cachedQuery;
            }
            else
            {
                cachedQuery = _cache[queryType];
            }

            return cachedQuery.CreateHandler<TOut>(query);
        }


        private CachedQuery buildCachedQuery<TDoc, TOut>(Type queryType, ICompiledQuery<TDoc,TOut> query)
        {
            Expression expression = query.QueryIs();
            var invocation = Expression.Invoke(expression, Expression.Parameter(typeof(IMartenQueryable<TDoc>)));

            var setters = findSetters(_schema.MappingFor(typeof(TDoc)).ToQueryableDocument(), queryType, expression, _serializer);

            var model = MartenQueryParser.TransformQueryFlyweight.GetParsedQuery(invocation);
            _schema.EnsureStorageExists(typeof(TDoc));

            var includeJoins = new IIncludeJoin[0];

            if (model.HasOperator<IncludeResultOperator>())
            {
                var builder = new CompiledIncludeJoinBuilder<TDoc, TOut>(_schema);
                includeJoins = builder.BuildIncludeJoins(model, query);
            }
            
            if (model.HasOperator<StatsResultOperator>())
            {
                SetStats(query, model);
            }

            var handler = _schema.HandlerFactory.BuildHandler<TOut>(model, includeJoins, Stats);
            var cmd = new NpgsqlCommand();
            handler.ConfigureCommand(cmd);

            return new CachedQuery
            {
                Command = cmd,
                ParameterSetters = setters,
                Handler = handler
            };
        }

        // TODO -- this can't be on QueryHandlerFactory!
        private void SetStats<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, QueryModel model)
        {
            var statsOperator = model.FindOperators<StatsResultOperator>().First();
            var propExp = (MemberExpression) statsOperator.Stats.Body;
            var prop = ((PropertyInfo) propExp.Member).SetMethod;
            Stats = new QueryStatistics();
            prop.Invoke(query, new[] {Stats});
        }

        private static IList<IDbParameterSetter> findSetters(IQueryableDocument mapping, Type queryType, Expression expression, ISerializer serializer)
        {
            var visitor = new CompiledQueryMemberExpressionVisitor(mapping, queryType, serializer);
            visitor.Visit(expression);
            var parameterSetters = visitor.ParameterSetters;
            return parameterSetters;
        }

        public ISelector<T> BuildSelector<T>(QueryModel query,
    IIncludeJoin[] joins, QueryStatistics stats)
        {
            var mapping = _schema.MappingFor(query).ToQueryableDocument();
            var selector = buildSelector<T>(_schema, mapping, query);

            if (stats != null)
            {
                selector = new StatsSelector<T>(stats, selector);
            }

            if (joins.Any())
            {
                selector = new IncludeSelector<T>(_schema, selector, joins);
            }

            return selector;
        }

        private static ISelector<T> buildSelector<T>(IDocumentSchema schema, IQueryableDocument mapping,
            QueryModel query)
        {
            var selectable = query.AllResultOperators().OfType<ISelectableOperator>().FirstOrDefault();
            if (selectable != null)
            {
                return selectable.BuildSelector<T>(schema, mapping);
            }


            if (query.HasSelectMany())
            {
                return new SelectManyQuery(mapping, query, 0).ToSelector<T>(schema.StoreOptions.Serializer());
            }

            if (query.SelectClause.Selector.Type == query.SourceType())
            {
                if (typeof(T) == typeof(string))
                {
                    return (ISelector<T>)new JsonSelector();
                }

                // I'm so ashamed of this hack, but "simplest thing that works"
                if (typeof(T) == typeof(IEvent))
                {
                    return mapping.As<EventQueryMapping>().Selector.As<ISelector<T>>();
                }

                if (typeof(T) != query.SourceType())
                {
                    // TODO -- going to have to come back to this one.
                    return null;
                }

                var resolver = schema.ResolverFor<T>();

                return new WholeDocumentSelector<T>(mapping, resolver);
            }


            var visitor = new SelectorParser(query);
            visitor.Visit(query.SelectClause.Selector);

            return visitor.ToSelector<T>(schema, mapping);
        }
    }
}
