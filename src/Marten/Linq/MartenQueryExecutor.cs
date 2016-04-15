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

namespace Marten.Linq
{
    public class MartenQueryExecutor : IMartenQueryExecutor
    {
        public MartenQueryExecutor(IManagedConnection runner, IDocumentSchema schema, IIdentityMap identityMap)
        {
            Schema = schema;
            IdentityMap = identityMap;
            Connection = runner;
        }

        private readonly IList<IIncludeJoin> _includes = new List<IIncludeJoin>();

        public IEnumerable<IIncludeJoin> Includes => _includes;

        public void AddInclude(IIncludeJoin include)
        {
            _includes.Add(include);
        }

        public IDocumentSchema Schema { get; }

        public IManagedConnection Connection { get; }

        public IIdentityMap IdentityMap { get; }

        public QueryPlan ExecuteExplain<T>(QueryModel queryModel)
        {
            ISelector<T> selector = null;
            var cmd = BuildCommand(queryModel, out selector);

            return Connection.ExplainQuery(cmd);
        }

        T IQueryExecutor.ExecuteScalar<T>(QueryModel queryModel)
        {
            var handler = Schema.HandlerFactory.HandlerForScalarQuery<T>(queryModel);
            return Connection.Execute(handler, IdentityMap);
        }


        T IQueryExecutor.ExecuteSingle<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
        {
            var handler = Schema.HandlerFactory.HandlerForSingleQuery<T>(queryModel, _includes.ToArray(),
                returnDefaultWhenEmpty);
            return Connection.Execute(handler, IdentityMap);

            /*
            var executors = new List<IScalarCommandBuilder<T>> {
                new MaxCommandBuilder<T>(_schema.Parser, _schema),
                new MinCommandBuilder<T>(_schema.Parser, _schema)
            };
            var queryExecution = executors.FirstOrDefault(_ => _.Match(queryModel));
            ISelector<T> selector;
            if (queryExecution != null)
            {
                var command = queryExecution.BuildCommand(queryModel, out selector);
                var resultSet = _runner.Resolve(command, selector, _identityMap);
                return resultSet.FirstOrDefault();
            }

            var cmd = buildCommand(queryModel, out selector);

            var enumerable = _runner.Resolve(cmd, selector, _identityMap);

            return GetResult(enumerable, queryModel);
            */
        }

        private NpgsqlCommand ExecuteScalar<T>(QueryModel queryModel, out ISelector<T> selector)
        {
            var executors = new List<IScalarCommandBuilder<T>>
            {
                new AnyCommandBuilder<T>(Schema.Parser, Schema),
                new CountCommandBuilder<T>(Schema.Parser, Schema),
                new LongCountCommandBuilder<T>(Schema.Parser, Schema),
                new SumCommandBuilder<T>(Schema.Parser, Schema),
                new AverageCommandBuilder<T>(Schema.Parser, Schema)
            };
            var queryExecution = executors.FirstOrDefault(_ => _.Match(queryModel));
            if (queryExecution == null) throw new NotSupportedException();
            var cmd = queryExecution.BuildCommand(queryModel, out selector);
            return cmd;
        }

        IEnumerable<T> IQueryExecutor.ExecuteCollection<T>(QueryModel queryModel)
        {
            ISelector<T> selector;
            var cmd = buildCommand(queryModel, out selector);

            return Connection.Resolve(cmd, selector, IdentityMap);
        }

        Task<IList<T>> IMartenQueryExecutor.ExecuteCollectionAsync<T>(QueryModel queryModel, CancellationToken token)
        {
            ISelector<T> selector;
            var cmd = buildCommand(queryModel, out selector);

            return Connection.ResolveAsync(cmd, selector, IdentityMap, token);
        }

        public async Task<T> ExecuteAsync<T>(QueryModel queryModel, CancellationToken token)
        {
            var scalarExecutions = new List<IScalarCommandBuilder<T>>
            {
                new AnyCommandBuilder<T>(Schema.Parser, Schema),
                new CountCommandBuilder<T>(Schema.Parser, Schema),
                new LongCountCommandBuilder<T>(Schema.Parser, Schema),
                new SumCommandBuilder<T>(Schema.Parser, Schema),
                new AverageCommandBuilder<T>(Schema.Parser, Schema),
                new MaxCommandBuilder<T>(Schema.Parser, Schema),
                new MinCommandBuilder<T>(Schema.Parser, Schema)
            };

            ISelector<T> selector;
            NpgsqlCommand cmd;
            var queryExecution = scalarExecutions.FirstOrDefault(x => x.Match(queryModel));
            if (queryExecution != null)
            {
                cmd = queryExecution.BuildCommand(queryModel, out selector);
                var resultSet = await Connection.ResolveAsync(cmd, selector, IdentityMap, token).ConfigureAwait(false);
                return resultSet.FirstOrDefault();
            }
            cmd = buildCommand(queryModel, out selector);

            var enumerable = await Connection.ResolveAsync(cmd, selector, IdentityMap, token).ConfigureAwait(false);
            return GetResult(enumerable, queryModel);
        }

        public async Task<IEnumerable<string>> ExecuteCollectionToJsonAsync<T>(QueryModel queryModel,
            CancellationToken token)
        {
            ISelector<T> selector;
            var cmd = buildCommand(queryModel, out selector);
            return await Connection.ResolveAsync(cmd, new StringSelector(), IdentityMap, token).ConfigureAwait(false);
            //return await _runner.QueryJsonAsync(cmd, token).ConfigureAwait(false);
        }

        public IEnumerable<string> ExecuteCollectionToJson<T>(QueryModel queryModel)
        {
            ISelector<T> selector;
            var cmd = buildCommand(queryModel, out selector);
            var results = Connection.Resolve(cmd, new StringSelector(), IdentityMap);
            return results;
            //return _runner.QueryJson(cmd);
        }

        public async Task<string> ExecuteJsonAsync<T>(QueryModel queryModel, CancellationToken token)
        {
            ISelector<T> selector;
            var cmd = buildCommand(queryModel, out selector);

            var enumerable =
                await Connection.ResolveAsync(cmd, new StringSelector(), IdentityMap, token).ConfigureAwait(false);
            return GetResult(enumerable, queryModel);
        }

        public string ExecuteJson<T>(QueryModel queryModel)
        {
            ISelector<T> selector;
            var cmd = buildCommand(queryModel, out selector);

            var enumerable = Connection.Resolve(cmd, new StringSelector(), IdentityMap);
            return GetResult(enumerable, queryModel);
        }

        public NpgsqlCommand BuildCommand<T>(QueryModel queryModel, out ISelector<T> selector)
        {
            var scalarExecutions = new List<IScalarCommandBuilder<T>>
            {
                new AnyCommandBuilder<T>(Schema.Parser, Schema),
                new CountCommandBuilder<T>(Schema.Parser, Schema),
                new LongCountCommandBuilder<T>(Schema.Parser, Schema),
                new SumCommandBuilder<T>(Schema.Parser, Schema),
                new AverageCommandBuilder<T>(Schema.Parser, Schema),
                new MaxCommandBuilder<T>(Schema.Parser, Schema),
                new MinCommandBuilder<T>(Schema.Parser, Schema)
            };

            NpgsqlCommand cmd;
            var queryExecution = scalarExecutions.FirstOrDefault(x => x.Match(queryModel));
            if (queryExecution != null)
            {
                cmd = queryExecution.BuildCommand(queryModel, out selector);
                return cmd;
            }
            cmd = buildCommand(queryModel, out selector);
            return cmd;
        }

        private NpgsqlCommand buildCommand<T>(QueryModel queryModel, out ISelector<T> selector)
        {
            var query = Schema.ToDocumentQuery(queryModel);
            query.Includes.AddRange(Includes);

            var command = new NpgsqlCommand();
            selector = query.ConfigureCommand<T>(Schema, command);

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
                throw new InvalidOperationException(
                    "Marten does not support Last()/LastOrDefault(). Use ordering and First()/FirstOrDefault() instead");
            }

            if (choiceResultOperator is SingleResultOperator || choiceResultOperator is FirstResultOperator)
            {
                return all.Single();
            }

            throw new NotSupportedException();
        }
    }
}