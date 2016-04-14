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
            var model = new MartenQueryParser().GetParsedQuery(Expression);
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
            var schema = executor.Schema;
            var rootType = model.MainFromClause.ItemType;
            var mapping = schema.MappingFor(rootType);

            return new DocumentQuery(mapping, model, schema.Parser);
        }

        public NpgsqlCommand BuildCommand(FetchType fetchType)
        {
            // Need to do each fetch type
            var model = new MartenQueryParser().GetParsedQuery(Expression);
            var executor = Provider.As<MartenQueryProvider>().Executor.As<MartenQueryExecutor>();
            var schema = executor.Schema;
            var rootType = model.MainFromClause.ItemType;
            var mapping = schema.MappingFor(rootType);

            var query = new DocumentQuery(mapping, model, schema.Parser);

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