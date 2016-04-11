using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Remotion.Linq;
using Remotion.Linq.Parsing.Structure;

namespace Marten.Linq
{
    public class MartenQueryProvider : QueryProviderBase, IMartenQueryProvider
    {
        private readonly Type _queryableType;

        public MartenQueryProvider(Type queryableType, IQueryParser queryParser, IQueryExecutor executor) : base(queryParser, executor)
        {
            _queryableType = queryableType;
        }

        public override IQueryable<T> CreateQuery<T>(Expression expression)
        {
            return (IQueryable<T>)Activator.CreateInstance(_queryableType.MakeGenericType(typeof(T)), this, expression);
        }

        public Task<IList<T>> ExecuteCollectionAsync<T>(Expression expression, CancellationToken token)
        {
            var queryModel = QueryParser.GetParsedQuery(expression);
            var queryExecutor = (IMartenQueryExecutor)Executor;
            return queryExecutor.ExecuteCollectionAsync<T>(queryModel, token);
        }

        public Task<IEnumerable<string>> ExecuteJsonCollectionAsync<T>(Expression expression, CancellationToken token)
        {
            var queryModel = QueryParser.GetParsedQuery(expression);
            var queryExecutor = (IMartenQueryExecutor)Executor;
            return queryExecutor.ExecuteCollectionToJsonAsync<T>(queryModel, token);
        }

        public IEnumerable<string> ExecuteJsonCollection<T>(Expression expression)
        {
            var queryModel = QueryParser.GetParsedQuery(expression);
            var queryExecutor = (IMartenQueryExecutor)Executor;
            return queryExecutor.ExecuteCollectionToJson<T>(queryModel);
        }

        public Task<T> ExecuteAsync<T>(Expression expression, CancellationToken token)
        {
            var queryModel = QueryParser.GetParsedQuery(expression);
            var queryExecutor = (IMartenQueryExecutor)Executor;
            return queryExecutor.ExecuteAsync<T>(queryModel, token);
        }

        public Task<string> ExecuteJsonAsync<T>(Expression expression, CancellationToken token)
        {
            var queryModel = QueryParser.GetParsedQuery(expression);
            var queryExecutor = (IMartenQueryExecutor)Executor;
            return queryExecutor.ExecuteJsonAsync<T>(queryModel, token);
        }

        public string ExecuteJson<T>(Expression expression)
        {
            var queryModel = QueryParser.GetParsedQuery(expression);
            var queryExecutor = (IMartenQueryExecutor)Executor;
            return queryExecutor.ExecuteJson<T>(queryModel);
        }
    }
}