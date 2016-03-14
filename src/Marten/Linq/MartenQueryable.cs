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