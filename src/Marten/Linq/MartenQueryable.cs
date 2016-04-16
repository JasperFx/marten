using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Includes;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses.ResultOperators;

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

        public QueryPlan Explain()
        {
            var model = new MartenQueryParser().GetParsedQuery(Expression);
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();

            return executor.ExecuteExplain<T>(model);
        }

        public Task<IList<T>> ExecuteCollectionAsync(CancellationToken token)
        {
            var queryProvider = (IMartenQueryProvider) Provider;
            return queryProvider.ExecuteCollectionAsync<T>(Expression, token);
        }

        public Task<IEnumerable<string>> ExecuteCollectionToJsonAsync(CancellationToken token)
        {
            var queryProvider = (IMartenQueryProvider) Provider;
            return queryProvider.ExecuteJsonCollectionAsync<T>(Expression, token);
        }

        public IEnumerable<string> ExecuteCollectionToJson()
        {
            var queryProvider = (IMartenQueryProvider) Provider;
            return queryProvider.ExecuteJsonCollection<T>(Expression);
        }

        public IEnumerable<IIncludeJoin> Includes
        {
            get
            {
                var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
                return executor.Includes;
            }
        }

        public IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback,
            JoinType joinType = JoinType.Inner) where TInclude : class
        {
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
            var schema = executor.Schema;

            schema.EnsureStorageExists(typeof (TInclude));

            var mapping = schema.MappingFor(typeof (T));
            var included = schema.MappingFor(typeof (TInclude));

            var visitor = new FindMembers();
            visitor.Visit(idSource);
            var members = visitor.Members.ToArray();

            var include = mapping.JoinToInclude(joinType, included, members, callback);

            executor.AddInclude(include);

            return this;
        }


        public IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, IList<TInclude> list,
            JoinType joinType = JoinType.Inner) where TInclude : class
        {
            return Include<TInclude>(idSource, list.Fill);
        }

        public IMartenQueryable<T> Include<TInclude, TKey>(Expression<Func<T, object>> idSource,
            IDictionary<TKey, TInclude> dictionary, JoinType joinType = JoinType.Inner) where TInclude : class
        {
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
            var schema = executor.Schema;

            var storage = schema.StorageFor(typeof (TInclude));

            return Include<TInclude>(idSource, x =>
            {
                var id = storage.Identity(x).As<TKey>();
                if (!dictionary.ContainsKey(id))
                {
                    dictionary.Add(id, x);
                }
            });
        }

        

        public DocumentQuery ToDocumentQuery()
        {
            var model = ToQueryModel();

            return Schema.ToDocumentQuery(model);
        }

        public IDocumentSchema Schema => Executor.Schema;

        public MartenQueryExecutor Executor => Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();

        public QueryModel ToQueryModel()
        {
            return MartenQueryParser.Flyweight.GetParsedQuery(Expression);
        }

        // TODO -- maybe move this out
        public NpgsqlCommand BuildCommand(FetchType fetchType)
        {
            // Need to do each fetch type
            var model = new MartenQueryParser().GetParsedQuery(Expression);
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
            var schema = executor.Schema;

            var query = schema.ToDocumentQuery(model);

            var parser = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
            query.Includes.AddRange(parser.Includes);

            var cmd = new NpgsqlCommand();

            switch (fetchType)
            {
                case FetchType.Count:
                    query.ConfigureForCount(cmd);
                    break;

                case FetchType.Any:
                    query.ConfigureForAny(cmd);
                    break;

                case FetchType.FetchMany:
                    query.ConfigureCommand<T>(schema, cmd);
                    break;

                case FetchType.FetchOne:
                    model.ResultOperators.Add(new TakeResultOperator(Expression.Constant(1)));
                    query.ConfigureCommand<T>(schema, cmd, 1);
                    break;
            }

            return cmd;
        }


        private Task<TResult> executeAsync<TResult>(Func<QueryModel, IQueryHandler<TResult>> source, CancellationToken token)
        {
            var query = ToQueryModel();
            Schema.EnsureStorageExists(query.SourceType());

            var handler = source(query);

            return Executor.Connection.FetchAsync(handler, Executor.IdentityMap, token);
        }

        public Task<bool> AnyAsync(CancellationToken token)
        {
            return executeAsync(q => new AnyQueryHandler(q, Schema), token);
        }

        public Task<int> CountAsync(CancellationToken token)
        {
            return executeAsync(q => new CountQueryHandler<int>(q, Schema), token);
        }

        public Task<long> CountLongAsync(CancellationToken token)
        {
            return executeAsync(q => new CountQueryHandler<long>(q, Schema), token);
        }

        public Task<TResult> FirstAsync<TResult>(CancellationToken token)
        {
            return executeAsync(q => OneResultHandler<TResult>.First(Schema, q, Includes.ToArray()), token);
        }

        public Task<TResult> FirstOrDefaultAsync<TResult>(CancellationToken token)
        {
            return executeAsync(q => OneResultHandler<TResult>.FirstOrDefault(Schema, q, Includes.ToArray()), token);
        }

        public Task<TResult> SingleAsync<TResult>(CancellationToken token)
        {
            return executeAsync(q => OneResultHandler<TResult>.Single(Schema, q, Includes.ToArray()), token);
        }

        public Task<TResult> SingleOrDefaultAsync<TResult>(CancellationToken token)
        {
            return executeAsync(q => OneResultHandler<TResult>.SingleOrDefault(Schema, q, Includes.ToArray()), token);
        }

        public Task<TResult> SumAsync<TResult>(CancellationToken token)
        {
            return executeAsync(q => AggregateQueryHandler<TResult>.Sum(Schema, q), token);
        }

        public Task<TResult> MinAsync<TResult>(CancellationToken token)
        {
            return executeAsync(q => AggregateQueryHandler<TResult>.Min(Schema, q), token);
        }

        public Task<TResult> MaxAsync<TResult>(CancellationToken token)
        {
            return executeAsync(q => AggregateQueryHandler<TResult>.Max(Schema, q), token);
        }

        public Task<double> AverageAsync(CancellationToken token)
        {
            return executeAsync(q => AggregateQueryHandler<double>.Average(Schema, q), token);
        }
    }
}