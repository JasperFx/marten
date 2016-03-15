using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq
{
    internal interface IScalarQueryExecution<TResult>
    {
        bool Match(QueryModel queryModel);
        TResult Execute(QueryModel queryModel);
        Task<TResult> ExecuteAsync(QueryModel queryModel, CancellationToken token);
    }

    abstract class ScalarQueryExecution<TOperator, TResult> : IScalarQueryExecution<TResult> where TOperator : ResultOperatorBase
    {
        protected readonly IManagedConnection _runner;
        protected readonly IDocumentSchema _schema;
        protected readonly MartenExpressionParser _expressionParser;

        protected ScalarQueryExecution(MartenExpressionParser expressionParser, IDocumentSchema schema, IManagedConnection runner)
        {
            _expressionParser = expressionParser;
            _schema = schema;
            _runner = runner;
        }

        public bool Match(QueryModel queryModel)
        {
            return queryModel.ResultOperators.OfType<TOperator>().Any();
        }
        public abstract TResult Execute(QueryModel queryModel);
        public abstract Task<TResult> ExecuteAsync(QueryModel queryModel, CancellationToken token);
    }

    class SumQueryExecution<TResult> : ScalarQueryExecution<SumResultOperator, TResult>
    {
        public SumQueryExecution(MartenExpressionParser expressionParser, IDocumentSchema schema, IManagedConnection runner)
            : base(expressionParser, schema, runner)
        { }

        public override TResult Execute(QueryModel queryModel)
        {
            var sumCommand = GetSumCommand(queryModel);

            return _runner.Execute(sumCommand, c => {
                var returnValue = c.ExecuteScalar();
                return Convert.ChangeType(returnValue, typeof(TResult)).As<TResult>();
            });
        }

        public override Task<TResult> ExecuteAsync(QueryModel queryModel, CancellationToken token)
        {
            var sumCommand = GetSumCommand(queryModel);

            return _runner.ExecuteAsync(sumCommand, async (c, tkn) => {
                var returnValue = await c.ExecuteScalarAsync(tkn).ConfigureAwait(false);
                return typeof(TResult).IsNullable() ? 
                Convert.ChangeType(returnValue, typeof(TResult).GetInnerTypeFromNullable()).As<TResult>() 
                : Convert.ChangeType(returnValue, typeof(TResult)).As<TResult>();
            }, token);
        }

        private NpgsqlCommand GetSumCommand(QueryModel queryModel)
        {
            var mapping = _schema.MappingFor(queryModel.MainFromClause.ItemType);
            var documentQuery = new DocumentQuery(mapping, queryModel, _expressionParser);

            _schema.EnsureStorageExists(mapping.DocumentType);
            var sumCommand = new NpgsqlCommand();
            documentQuery.ConfigureForSum(sumCommand);
            return sumCommand;
        }
    }

    class AnyQueryExecution<TResult> : ScalarQueryExecution<AnyResultOperator, TResult>
    {
        public AnyQueryExecution(MartenExpressionParser expressionParser, IDocumentSchema schema, IManagedConnection runner)
            : base(expressionParser, schema, runner){ }

        public override TResult Execute(QueryModel queryModel)
        {
            var anyCommand = GetAnyCommand(queryModel);

            return _runner.Execute(anyCommand, c => (TResult)c.ExecuteScalar());
        }

        public override Task<TResult> ExecuteAsync(QueryModel queryModel, CancellationToken token)
        {
            var anyCommand = GetAnyCommand(queryModel);

            return _runner.ExecuteAsync(anyCommand, async (c, tkn) =>
            {
                var result = await c.ExecuteScalarAsync(tkn).ConfigureAwait(false);
                return (TResult)result;
            }, token);
        }

        private NpgsqlCommand GetAnyCommand(QueryModel queryModel)
        {
            var mapping = _schema.MappingFor(queryModel.SelectClause.Selector.Type);
            var documentQuery = new DocumentQuery(mapping, queryModel, _expressionParser);

            _schema.EnsureStorageExists(mapping.DocumentType);

            var anyCommand = new NpgsqlCommand();
            documentQuery.ConfigureForAny(anyCommand);
            return anyCommand;
        }
    }

    class CountQueryExecution<TResult> : ScalarQueryExecution<CountResultOperator, TResult>
    {
        public CountQueryExecution(MartenExpressionParser expressionParser, IDocumentSchema schema, IManagedConnection runner)
            : base(expressionParser, schema, runner){ }

        public override TResult Execute(QueryModel queryModel)
        {
            var countCommand = GetCountCommand(queryModel);

            return _runner.Execute(countCommand, c =>
            {
                var returnValue = c.ExecuteScalar();
                return Convert.ToInt32(returnValue).As<TResult>();
            });
        }

        public override Task<TResult> ExecuteAsync(QueryModel queryModel, CancellationToken token)
        {
            var countCommand = GetCountCommand(queryModel);

            return _runner.ExecuteAsync(countCommand, async (c, tkn) =>
            {
                var returnValue = await c.ExecuteScalarAsync(tkn).ConfigureAwait(false);
                return Convert.ToInt32(returnValue).As<TResult>();
            }, token);
        }

        private NpgsqlCommand GetCountCommand(QueryModel queryModel)
        {
            var mapping = _schema.MappingFor(queryModel.SelectClause.Selector.Type);
            var documentQuery = new DocumentQuery(mapping, queryModel, _expressionParser);

            _schema.EnsureStorageExists(mapping.DocumentType);

            var countCommand = new NpgsqlCommand();
            documentQuery.ConfigureForCount(countCommand);
            return countCommand;
        }
    }

    class LongCountQueryExecution<TResult> : ScalarQueryExecution<LongCountResultOperator, TResult> {
        public LongCountQueryExecution(MartenExpressionParser expressionParser, IDocumentSchema schema, IManagedConnection runner) : base(expressionParser, schema, runner)
        {
        }

        public override TResult Execute(QueryModel queryModel)
        {
            throw new NotImplementedException();
        }

        public override Task<TResult> ExecuteAsync(QueryModel queryModel, CancellationToken token)
        {
            var countCommand = GetLongCountCommand(queryModel);

            return _runner.ExecuteAsync(countCommand, async (c, tkn) =>
            {
                var returnValue = await c.ExecuteScalarAsync(tkn).ConfigureAwait(false);
                return Convert.ToInt64(returnValue).As<TResult>();
            }, token);
        }

        private NpgsqlCommand GetLongCountCommand(QueryModel queryModel)
        {
            var mapping = _schema.MappingFor(queryModel.SelectClause.Selector.Type);
            var documentQuery = new DocumentQuery(mapping, queryModel, _expressionParser);

            _schema.EnsureStorageExists(mapping.DocumentType);

            var countCommand = new NpgsqlCommand();
            documentQuery.ConfigureForCount(countCommand);
            return countCommand;
        }
    }
}