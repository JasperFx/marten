using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Services;
using Npgsql;
using Remotion.Linq.Parsing.Structure;

namespace Marten.Linq
{
    public interface ICompiledQueryExecutor
    {
        IEnumerable<TOut> ExecuteQuery<TDoc, TOut>(IQuerySession session, IEnumerableCompiledQuery<TDoc, TOut> query);
        Task<IEnumerable<TOut>> ExecuteQueryAsync<TDoc, TOut>(IQuerySession session, IEnumerableCompiledQuery<TDoc, TOut> query, CancellationToken token);
        TOut ExecuteQuery<TDoc, TOut>(IQuerySession session, ICompiledQuery<TDoc, TOut> query);
        Task<TOut> ExecuteQueryAsync<TDoc, TOut>(IQuerySession session, ICompiledQuery<TDoc, TOut> query, CancellationToken token);
    }

    public class CompiledQueryExecutor : ICompiledQueryExecutor
    {
        private readonly IQueryParser _parser;
        private readonly ConcurrentCache<Type, CachedQuery> _cache = new ConcurrentCache<Type, CachedQuery>();

        public CompiledQueryExecutor(IQueryParser parser)
        {
            _parser = parser;
        }

        public IEnumerable<TOut> ExecuteQuery<TDoc, TOut>(IQuerySession session, IEnumerableCompiledQuery<TDoc, TOut> query)
        {
            var queryable = session.Query<TDoc>().As<MartenQueryable<TDoc>>();
            var provider = queryable.Provider.As<MartenQueryProvider>();
            var executor = provider.Executor.As<MartenQueryExecutor>();
            var cachedQuery = GetOrAddCachedQuery<TDoc, TOut>(query.GetType(), query.QueryIs, queryable, executor);
            var command = PrepareCommand(query, cachedQuery);
            return executor.Connection.Resolve(command, cachedQuery.Selector.As<ISelector<TOut>>(), executor.IdentityMap);
        }

        public Task<IEnumerable<TOut>> ExecuteQueryAsync<TDoc, TOut>(IQuerySession session, IEnumerableCompiledQuery<TDoc, TOut> query, CancellationToken token)
        {
            var queryable = session.Query<TDoc>().As<MartenQueryable<TDoc>>();
            var executor = queryable.Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
            var cachedQuery = GetOrAddCachedQuery<TDoc, TOut>(query.GetType(), query.QueryIs, queryable, executor);
            var command = PrepareCommand(query, cachedQuery);
            return executor.Connection.ResolveAsync(command, cachedQuery.Selector.As<ISelector<TOut>>(), executor.IdentityMap, token);
        }

        public async Task<TOut> ExecuteQueryAsync<TDoc, TOut>(IQuerySession session, ICompiledQuery<TDoc, TOut> query, CancellationToken token)
        {
            var queryable = session.Query<TDoc>().As<MartenQueryable<TDoc>>();
            var executor = queryable.Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
            var cachedQuery = GetOrAddCachedQuery<TDoc, TOut>(query.GetType(), query.QueryIs, queryable, executor);
            var command = PrepareCommand(query, cachedQuery);
            var results = await executor.Connection.ResolveAsync(command, cachedQuery.Selector.As<ISelector<TOut>>(), executor.IdentityMap, token).ConfigureAwait(false);
            return results.Single();
        }

        public TOut ExecuteQuery<TDoc, TOut>(IQuerySession session, ICompiledQuery<TDoc, TOut> query)
        {
            var queryable = session.Query<TDoc>().As<MartenQueryable<TDoc>>();
            var executor = queryable.Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
            var cachedQuery = GetOrAddCachedQuery<TDoc, TOut>(query.GetType(), query.QueryIs, queryable, executor);
            var command = PrepareCommand(query, cachedQuery);
            return executor.Connection.Resolve(command, cachedQuery.Selector.As<ISelector<TOut>>(), executor.IdentityMap).Single();
        }

        private NpgsqlCommand PrepareCommand(object query, CachedQuery cachedQuery)
        {
            var command = cachedQuery.Command;
            for (int i = 0; i < command.Parameters.Count; ++i)
            {
                var setter = cachedQuery.ParameterSetters[i];
                var parameter = command.Parameters[i];
                setter.SetParameter(query, parameter);
            }
            return command;
        }

        private CachedQuery GetOrAddCachedQuery<TDoc, TOut>(Type queryType, Func<Expression> expression, MartenQueryable<TDoc> queryable,
            MartenQueryExecutor executor)
        {
            CachedQuery cachedQuery;
            if (!_cache.Has(queryType))
            {
                var invocationExpression = Expression.Invoke(expression(), queryable.Expression);
                var visitor = new CompiledQueryMemberExpressionVisitor(queryType);
                visitor.Visit(invocationExpression);
                var parameterSetters = visitor.ParameterSetters;
                var model = _parser.GetParsedQuery(invocationExpression);
                ISelector<TOut> selector;
                var cmd = executor.BuildCommand(model, out selector);
                cachedQuery = new CachedQuery
                {
                    Command = cmd,
                    ParameterSetters = parameterSetters,
                    Selector = selector
                };
                _cache[queryType] = cachedQuery;
            }
            else
            {
                cachedQuery = _cache[queryType];
            }

            return cachedQuery;
        }

        private class CachedQuery
        {
            private NpgsqlCommand _command;
            public object Selector { get; set; }

            public IList<IDbParameterSetter> ParameterSetters { get; set; }

            public NpgsqlCommand Command
            {
                get { return _command.Clone(); }
                set { _command = value; }
            }
        }
    }
}