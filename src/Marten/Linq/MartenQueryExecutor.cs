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

            ISelector<T> selector;
            var cmd = BuildCommand(queryModel, out selector);

            var enumerable = _runner.Resolve(cmd, selector, _identityMap);

            return GetResult(enumerable, queryModel);
        }

        IEnumerable<T> IQueryExecutor.ExecuteCollection<T>(QueryModel queryModel)
        {
            ISelector<T> selector;
            var cmd = BuildCommand(queryModel, out selector);

            return _runner.Resolve(cmd, selector, _identityMap);
        }

        Task<IEnumerable<T>> IMartenQueryExecutor.ExecuteCollectionAsync<T>(QueryModel queryModel, CancellationToken token)
        {
            ISelector<T> selector;
            var cmd = BuildCommand(queryModel, out selector);

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

            var queryExecution = scalarExecutions.FirstOrDefault(x => x.Match(queryModel));
            if (queryExecution != null)
            {
                return await queryExecution.ExecuteAsync(queryModel, token).ConfigureAwait(false);
            }

            ISelector<T> selector;
            var cmd = BuildCommand(queryModel, out selector);

            var enumerable = await _runner.ResolveAsync(cmd, selector, _identityMap, token).ConfigureAwait(false);
            return GetResult(enumerable, queryModel);
        }

        public async Task<IEnumerable<string>> ExecuteCollectionToJsonAsync<T>(QueryModel queryModel, CancellationToken token)
        {
            ISelector<T> selector;
            var cmd = BuildCommand(queryModel, out selector);
            return await _runner.QueryJsonAsync(cmd, token).ConfigureAwait(false);
        }

        public IEnumerable<string> ExecuteCollectionToJson<T>(QueryModel queryModel)
        {
            ISelector<T> selector;
            var cmd = BuildCommand(queryModel, out selector);
            return _runner.QueryJson(cmd);
        }

        public async Task<string> ExecuteJsonAsync<T>(QueryModel queryModel, CancellationToken token)
        {
            ISelector<T> selector;
            var cmd = BuildCommand(queryModel, out selector);

            var enumerable = await _runner.QueryJsonAsync(cmd, token).ConfigureAwait(false);
            return GetResult(enumerable, queryModel);
        }

        public string ExecuteJson<T>(QueryModel queryModel)
        {
            ISelector<T> selector;
            var cmd = BuildCommand(queryModel, out selector);

            var enumerable = _runner.QueryJson(cmd);
            return GetResult(enumerable, queryModel);
        }

        private NpgsqlCommand BuildCommand<T>(QueryModel queryModel, out ISelector<T> selector)
        {
            var mapping = _schema.MappingFor(queryModel.MainFromClause.ItemType);
            var query = new DocumentQuery(mapping, queryModel, _expressionParser);
            query.Includes.AddRange(Includes);

            _schema.EnsureStorageExists(mapping.DocumentType);

            var command = new NpgsqlCommand();
            selector = query.ConfigureCommand<T>(_schema, command);

            return command;
        }

        private T GetResult<T>(IEnumerable<T> enumerable, QueryModel queryModel)
        {
            var all = enumerable.ToList();
            var choiceResultOperator = queryModel.ResultOperators.OfType<ChoiceResultOperatorBase>().Single();
            if (choiceResultOperator.ReturnDefaultWhenEmpty && all.Count == 0)
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
    }
}