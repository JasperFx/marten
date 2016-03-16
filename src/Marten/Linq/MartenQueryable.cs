using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
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

        public string FirstJson(bool returnDefaultWhenEmpty)
        {
            var model = new MartenQueryParser().GetParsedQuery(Expression);
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();

            return executor.ExecuteFirstToJson<T>(model, returnDefaultWhenEmpty);
        }

        public Task<string> FirstJsonAsync(CancellationToken token, bool returnDefaultWhenEmpty)
        {
            var model = new MartenQueryParser().GetParsedQuery(Expression);
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();

            return executor.ExecuteFirstToJsonAsync<T>(model, returnDefaultWhenEmpty, token);
        }

        public IMartenQueryable<T> Include<TInclude>(Expression<Func<T, object>> idSource, Action<TInclude> callback) where TInclude : class
        {
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
            var schema = executor.Schema;

            var mapping = schema.MappingFor(typeof (T));
            var included = schema.MappingFor(typeof (TInclude));

            var visitor = new FindMembers();
            visitor.Visit(idSource);
            var members = visitor.Members.ToArray();

            var include = mapping.JoinToInclude<TInclude>(JoinType.Inner, included, members, callback);

            executor.AddInclude(include);

            return this;
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
                    query.ConfigureCommand<T>(schema, cmd);
                    break;

            }

            return cmd;
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