using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Includes;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Parsing.Structure;

namespace Marten.Linq
{
    public class MartenQueryExecutor : IMartenQueryExecutor
    {
        private readonly IQueryParser _parser;
        private readonly IIdentityMap _identityMap;
        private readonly IManagedConnection _runner;
        private readonly IDocumentSchema _schema;
        private readonly MartenExpressionParser _expressionParser;

        public MartenQueryExecutor(IManagedConnection runner, IDocumentSchema schema, MartenExpressionParser expressionParser, IQueryParser parser, IIdentityMap identityMap)
        {
            _schema = schema;
            _expressionParser = expressionParser;
            _parser = parser;
            _identityMap = identityMap;
            _runner = runner;
        }

        private readonly IList<IIncludeJoin> _includes = new List<IIncludeJoin>();

        public IEnumerable<IIncludeJoin> Includes => _includes;

        public void AddInclude(IIncludeJoin include)
        {
            _includes.Add(include);
        }

        public IDocumentSchema Schema => _schema;

        public MartenExpressionParser ExpressionParser => _expressionParser;

        private IResolver<T> resolver<T>()
        {
            return _schema.StorageFor(typeof (T)).As<IResolver<T>>();
        }

        public QueryPlan ExecuteExplain<T>(QueryModel queryModel)
        {
            ISelector<T> selector = null;
            var cmd = BuildCommand(queryModel, out selector);

            return _runner.ExplainQuery(cmd);
        }

        T IQueryExecutor.ExecuteScalar<T>(QueryModel queryModel)
        {
            var executors = new List<IScalarQueryExecution<T>> {
                new AnyQueryExecution<T>(_expressionParser, _schema, _runner),
                new CountQueryExecution<T>(_expressionParser, _schema, _runner),
                new LongCountQueryExecution<T>(_expressionParser, _schema, _runner),
                new SumQueryExecution<T>(_expressionParser, _schema, _runner),
                new AverageQueryExecution<T>(_expressionParser, _schema, _runner)
            };
            var queryExecution = executors.FirstOrDefault(_ => _.Match(queryModel));
            if (queryExecution == null) throw new NotSupportedException();
            return queryExecution.Execute(queryModel).As<T>();
        }

        T IQueryExecutor.ExecuteSingle<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
        {
            var executors = new List<IScalarQueryExecution<T>> {
                new MaxQueryExecution<T>(_expressionParser, _schema, _runner),
                new MinQueryExecution<T>(_expressionParser, _schema, _runner)
            };
            var queryExecution = executors.FirstOrDefault(_ => _.Match(queryModel));
            if (queryExecution != null) return queryExecution.Execute(queryModel).As<T>();

            var isLast = queryModel.ResultOperators.OfType<LastResultOperator>().Any();
            if (isLast)
            {
                throw new InvalidOperationException("Marten does not support Last()/LastOrDefault() querying. Reverse your ordering and use First()/FirstOrDefault() instead");
            }

            ISelector<T> selector = null;
            var cmd = BuildCommand<T>(queryModel, out selector);

            var all = _runner.Resolve(cmd, selector, _identityMap).ToArray();

            if (returnDefaultWhenEmpty && all.Length == 0) return default(T);

            return all.Single();
        }
        
        IEnumerable<T> IQueryExecutor.ExecuteCollection<T>(QueryModel queryModel)
        {
            ISelector<T> selector = null;
            var cmd = BuildCommand<T>(queryModel, out selector);

            return _runner.Resolve(cmd, selector, _identityMap);
        }

        public IEnumerable<string> ExecuteCollectionToJson<T>(QueryModel queryModel)
        {
            ISelector<T> selector = null;
            var cmd = BuildCommand(queryModel, out selector);

            return _runner.QueryJson(cmd);
        }

        public Task<IEnumerable<string>> ExecuteCollectionToJsonAsync<T>(QueryModel queryModel, CancellationToken token)
        {
            ISelector<T> selector = null;
            var cmd = BuildCommand(queryModel, out selector);

            return _runner.QueryJsonAsync(cmd, token);
        }

        public string ExecuteSingleToJson<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
        {
            var all = executeJson<T>(queryModel, new SingleResultOperator(returnDefaultWhenEmpty));
            return returnDefaultWhenEmpty ? all.SingleOrDefault() : all.Single();
        }

        public Task<string> ExecuteSingleToJsonAsync<T>(QueryModel queryModel, bool returnDefaultWhenEmpty, CancellationToken token)
        {
            var cmd = prepareCommand<T>(queryModel, new SingleResultOperator(returnDefaultWhenEmpty));

            return _runner.QueryJsonAsync(cmd, token).ContinueWith(task =>
            {
                var all = task.Result.ToArray();
                return returnDefaultWhenEmpty ? all.SingleOrDefault() : all.Single();
            }, token);
        }

        public string ExecuteFirstToJson<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
        {
            var all = executeJson<T>(queryModel, new FirstResultOperator(returnDefaultWhenEmpty));
            return GetFirst(returnDefaultWhenEmpty, all.ToArray());
        }

        public Task<string> ExecuteFirstToJsonAsync<T>(QueryModel queryModel, bool returnDefaultWhenEmpty, CancellationToken token)
        {
            var cmd = prepareCommand<T>(queryModel, new FirstResultOperator(returnDefaultWhenEmpty));

            return _runner.QueryJsonAsync(cmd, token).ContinueWith(task =>
            {
                var all = task.Result.ToArray();
                return GetFirst(returnDefaultWhenEmpty, all);
            }, token);
        }

        private static string GetFirst(bool returnDefaultWhenEmpty, string[] all)
        {
            if (returnDefaultWhenEmpty && !all.Any()) return null;

            return all.First();
        }

        private IEnumerable<string> executeJson<T>(QueryModel queryModel, ResultOperatorBase resultOperator)
        {
            var cmd = prepareCommand<T>(queryModel, resultOperator);
            var queryResult = _runner.QueryJson(cmd);
            return queryResult;
        }

        private NpgsqlCommand prepareCommand<T>(QueryModel queryModel, ResultOperatorBase resultOperator)
        {
            queryModel.ResultOperators.Add(resultOperator);
            var cmd = new NpgsqlCommand();
            var mapping = _schema.MappingFor(queryModel.MainFromClause.ItemType);
            var documentQuery = new DocumentQuery(mapping, queryModel, _expressionParser);
            documentQuery.ConfigureCommand<T>(_schema, cmd);
            return cmd;
        }

        public NpgsqlCommand  BuildCommand<T>(QueryModel queryModel, out ISelector<T> selector)
        {
            var mapping = _schema.MappingFor(queryModel.MainFromClause.ItemType);
            var query = new DocumentQuery(mapping, queryModel, _expressionParser);
            query.Includes.AddRange(Includes);

            _schema.EnsureStorageExists(mapping.DocumentType);

            var command = new NpgsqlCommand();
            selector = query.ConfigureCommand<T>(_schema, command);

            return command;
        }

        Task<IEnumerable<T>> IMartenQueryExecutor.ExecuteCollectionAsync<T>(QueryModel queryModel, CancellationToken token)
        {
            ISelector<T> selector = null;
            var cmd = BuildCommand<T>(queryModel, out selector);

            return _runner.ResolveAsync(cmd, selector, _identityMap, token);
        }

        public async Task<T> ExecuteAsync<T>(QueryModel queryModel, CancellationToken token)
        {
            var scalarExecutions = new List<IScalarQueryExecution<T>> {
                new AnyQueryExecution<T>(_expressionParser, _schema, _runner),
                new CountQueryExecution<T>(_expressionParser, _schema, _runner),
                new LongCountQueryExecution<T>(_expressionParser, _schema, _runner),
                new SumQueryExecution<T>(_expressionParser, _schema, _runner),
                new AverageQueryExecution<T>(_expressionParser, _schema, _runner),
                new MaxQueryExecution<T>(_expressionParser, _schema, _runner),
                new MinQueryExecution<T>(_expressionParser, _schema, _runner),
            };

            if (scalarExecutions.Any(ex=>ex.Match(queryModel)))
                return await ExecuteScalarAsync<T>(queryModel, token, scalarExecutions).ConfigureAwait(false);
            

            var choiceResultOperator = queryModel.ResultOperators.OfType<ChoiceResultOperatorBase>().Single();

            ISelector<T> selector = null;
            var cmd = BuildCommand<T>(queryModel, out selector);

            var enumerable = await _runner.ResolveAsync(cmd, selector, _identityMap, token).ConfigureAwait(false);
            var all = enumerable.ToArray();

            if (choiceResultOperator.ReturnDefaultWhenEmpty && all.Length == 0)
            {
                return default(T);
            }

            if (choiceResultOperator is LastResultOperator)
            {
                throw new InvalidOperationException("Marten does not support Last()/LastOrDefault(). Use ordering and First()/FirstOrDefault() instead");
            }

            if (choiceResultOperator is SingleResultOperator || choiceResultOperator is FirstResultOperator)
            {
                return all.Single();
            }

            throw new NotSupportedException();
        }

        private Task<T> ExecuteScalarAsync<T>(QueryModel queryModel, CancellationToken token, List<IScalarQueryExecution<T>> executors)
        {
            var queryExecution = executors.FirstOrDefault(_ => _.Match(queryModel));
            if (queryExecution == null) throw new NotSupportedException();
            return queryExecution.ExecuteAsync(queryModel, token);
        }
    }
}