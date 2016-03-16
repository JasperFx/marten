using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
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

        public Task<IEnumerable<T>> ExecuteCollectionAsync(CancellationToken token)
        {
            var queryProvider = (IMartenQueryProvider)Provider;
            return queryProvider.ExecuteCollectionAsync<T>(Expression, token);
        }

        public string ToListJson()
        {
            var model = new MartenQueryParser().GetParsedQuery(Expression);
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();

            var listJsons = executor.ExecuteCollectionToJson<T>(model).ToArray();
            return $"[{listJsons.Join(",")}]";
        }

        public Task<string> ToListJsonAsync(CancellationToken token)
        {
            var model = new MartenQueryParser().GetParsedQuery(Expression);
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();

            return executor.ExecuteCollectionToJsonAsync<T>(model, token).ContinueWith(task => $"[{task.Result.Join(",")}]", token);
        }

        public string FirstOrDefaultJson()
        {
            var model = new MartenQueryParser().GetParsedQuery(Expression);
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();

            return executor.ExecuteFirstToJson<T>(model, true);
        }

        public Task<string> FirstOrDefaultJsonAsync(CancellationToken token)
        {
            var model = new MartenQueryParser().GetParsedQuery(Expression);
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();

            return executor.ExecuteFirstToJsonAsync<T>(model, true, token);
        }

        public NpgsqlCommand BuildCommand(FetchType fetchType)
        {
            // Need to do each fetch type
            var model = new MartenQueryParser().GetParsedQuery(Expression);
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
            var schema = executor.Schema;
            var rootType = model.MainFromClause.ItemType;
            var mapping = schema.MappingFor(rootType);
            
            var query = new DocumentQuery(mapping, model, executor.ExpressionParser);
            
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
                    query.ConfigureCommand<T>(schema, cmd);
                    break;

            }

            return cmd;
        }
    }

    public static class QueryableExtensions
    {
        public static NpgsqlCommand ToCommand<T>(this IQueryable<T> queryable, FetchType fetchType)
        {
            var q = queryable as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{nameof(ToCommand)} is only valid on Marten IQueryable objects");
            }

            return q.BuildCommand(fetchType);
        }

        public static string ToListJson<T>(this IQueryable<T> queryable)
        {
            var q = queryable as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{nameof(ToListJson)} is only valid on Marten IQueryable objects");
            }

            return q.ToListJson();
        }

        public static Task<string> ToListJsonAsync<T>(this IQueryable<T> queryable, CancellationToken token = default(CancellationToken))
        {
            var q = queryable as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{nameof(ToListJson)} is only valid on Marten IQueryable objects");
            }

            return q.ToListJsonAsync(token);
        }

        public static string FirstOrDefaultJson<T>(this IQueryable<T> queryable, Expression<Func<T,bool>> expression)
        {
            var q = queryable.Where(expression) as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{nameof(FirstOrDefaultJson)} is only valid on Marten IQueryable objects");
            }

            return q.FirstOrDefaultJson();
        }

        public static Task<string> FirstOrDefaultJsonAsync<T>(this IQueryable<T> queryable, Expression<Func<T,bool>> expression, CancellationToken token = default(CancellationToken))
        {
            var q = queryable.Where(expression) as MartenQueryable<T>;

            if (q == null)
            {
                throw new InvalidOperationException($"{nameof(FirstOrDefaultJson)} is only valid on Marten IQueryable objects");
            }

            return q.FirstOrDefaultJsonAsync(token);
        }
    }

    /// <summary>
    /// In basic terms, how is the IQueryable going to be executed?
    /// </summary>
    public enum FetchType
    {
        /// <summary>
        /// First/FirstOrDefault/Single/SingleOrDefault
        /// </summary>
        FetchOne,

        /// <summary>
        /// Any execution that returns an IEnumerable (ToArray()/ToList()/etc.)
        /// </summary>
        FetchMany,

        /// <summary>
        /// Using IQueryable.Count()
        /// </summary>
        Count,

        /// <summary>
        /// Using IQueryable.Any()
        /// </summary>
        Any
    }
}