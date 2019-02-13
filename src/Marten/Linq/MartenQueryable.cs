using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq.Model;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Marten.Services.Includes;
using Marten.Storage;
using Marten.Transforms;
using Marten.Util;
using Npgsql;
using Remotion.Linq;

namespace Marten.Linq
{
    public class MartenQueryable<T> : QueryableBase<T>, IMartenQueryable<T>
    {
        public MartenQueryable(IQueryProvider provider) : base(provider)
        {
        }

        public MartenQueryable(IQueryProvider provider, Expression expression) : base(provider, expression)
        {
        }

        public DocumentStore Store => Executor.Store;

        public ITenant Tenant => Executor.Tenant;

        public MartenQueryExecutor Executor => Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();

        public QueryPlan Explain(FetchType fetchType = FetchType.FetchMany, Action<IConfigureExplainExpressions> configureExplain = null)
        {
            var handler = toDiagnosticHandler(fetchType);

            var cmd = CommandBuilder.ToCommand(Tenant, handler);

            return Executor.As<MartenQueryExecutor>().Connection.ExplainQuery(cmd, configureExplain);
        }

        public IQueryable<TDoc> TransformTo<TDoc>(string transformName)
        {
            return this.Select(x => x.TransformTo<T, TDoc>(transformName));
        }

        public IEnumerable<IIncludeJoin> Includes
        {
            get
            {
                var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
                return executor.Includes;
            }
        }

        public QueryStatistics Statistics
        {
            get
            {
                var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
                return executor.Statistics;
            }
        }

        public IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback,
            JoinType joinType = JoinType.Inner)
        {
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
            var tenant = executor.Tenant;

            tenant.EnsureStorageExists(typeof(TInclude));

            var mapping = tenant.MappingFor(typeof(T)).ToQueryableDocument();
            var included = tenant.MappingFor(typeof(TInclude)).ToQueryableDocument();

            var visitor = new FindMembers();
            visitor.Visit(idSource);
            var members = visitor.Members.ToArray();

            var include = mapping.JoinToInclude(joinType, included, members, callback);

            executor.AddInclude(include);

            return this;
        }

        public IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list,
            JoinType joinType = JoinType.Inner)
        {
            return Include<TInclude>(idSource, list.Fill, joinType);
        }

        public IMartenQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource,
            IDictionary<TKey, TInclude> dictionary, JoinType joinType = JoinType.Inner)
        {
            var storage = Tenant.StorageFor(typeof(TInclude));

            return Include<TInclude>(idSource, x =>
            {
                var id = storage.Identity(x).As<TKey>();
                if (!dictionary.ContainsKey(id))
                {
                    dictionary.Add(id, x);
                }
            }, joinType);
        }

        public IMartenQueryable<T> Stats(out QueryStatistics stats)
        {
            stats = new QueryStatistics();
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
            executor.Statistics = stats;

            return this;
        }

        public Task<IReadOnlyList<TResult>> ToListAsync<TResult>(CancellationToken token)
        {
            return executeAsync(q => q.ToList().As<IQueryHandler<IReadOnlyList<TResult>>>(), token);
        }

        public Task<bool> AnyAsync(CancellationToken token)
        {
            return executeAsync(q => q.ToAny(), token);
        }

        public Task<int> CountAsync(CancellationToken token)
        {
            return executeAsync(q => q.ToCount<int>(), token);
        }

        public Task<long> CountLongAsync(CancellationToken token)
        {
            return executeAsync(q => q.ToCount<long>(), token);
        }

        public Task<TResult> FirstAsync<TResult>(CancellationToken token)
        {
            return executeAsync(q => OneResultHandler<TResult>.First(q.As<LinqQuery<TResult>>()), token);
        }

        public Task<TResult> FirstOrDefaultAsync<TResult>(CancellationToken token)
        {
            return executeAsync(q => OneResultHandler<TResult>.FirstOrDefault(q.As<LinqQuery<TResult>>()), token);
        }

        public Task<TResult> SingleAsync<TResult>(CancellationToken token)
        {
            return executeAsync(q => OneResultHandler<TResult>.Single(q.As<LinqQuery<TResult>>()), token);
        }

        public Task<TResult> SingleOrDefaultAsync<TResult>(CancellationToken token)
        {
            return executeAsync(q => OneResultHandler<TResult>.SingleOrDefault(q.As<LinqQuery<TResult>>()), token);
        }

        public Task<TResult> SumAsync<TResult>(CancellationToken token)
        {
            return executeAsync(AggregateQueryHandler<TResult>.Sum, token);
        }

        public Task<TResult> MinAsync<TResult>(CancellationToken token)
        {
            return executeAsync(AggregateQueryHandler<TResult>.Min, token);
        }

        public Task<TResult> MaxAsync<TResult>(CancellationToken token)
        {
            return executeAsync(AggregateQueryHandler<TResult>.Max, token);
        }

        public Task<double> AverageAsync(CancellationToken token)
        {
            return executeAsync(AggregateQueryHandler<double>.Average, token);
        }

        public QueryModel ToQueryModel()
        {
            return MartenQueryParser.Flyweight.GetParsedQuery(Expression);
        }

        public LinqQuery<T> ToLinqQuery()
        {
            var query = MartenQueryParser.Flyweight.GetParsedQuery(Expression);
            return new LinqQuery<T>(Store, query, Includes.ToArray(), Statistics);
        }

        private IQueryHandler toDiagnosticHandler(FetchType fetchType)
        {
            switch (fetchType)
            {
                case FetchType.Count:
                    return ToLinqQuery().ToCount<int>();

                case FetchType.Any:
                    return ToLinqQuery().ToAny();

                case FetchType.FetchMany:
                    return ToLinqQuery().ToList();

                case FetchType.FetchOne:
                    return OneResultHandler<T>.First(ToLinqQuery());
            }

            throw new ArgumentOutOfRangeException(nameof(fetchType));
        }

        public NpgsqlCommand BuildCommand(FetchType fetchType)
        {
            var handler = toDiagnosticHandler(fetchType);

            return CommandBuilder.ToCommand(Tenant, handler);
        }

        private Task<TResult> executeAsync<TResult>(Func<LinqQuery<T>, IQueryHandler<TResult>> source,
            CancellationToken token)
        {
            var query = ToQueryModel();
            Tenant.EnsureStorageExists(query.SourceType());

            var linq = new LinqQuery<T>(Store, query, Includes.ToArray(), Statistics);

            var handler = source(linq);

            return Executor.Connection.FetchAsync(handler, Executor.IdentityMap.ForQuery(), Statistics, Tenant, token);
        }
    }
}