using System.Linq;
using Marten.Schema;
using Marten.Services;
using Npgsql;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;

namespace Marten.Linq
{
    internal interface IScalarCommandBuilder<TResult>
    {
        bool Match(QueryModel queryModel);
        NpgsqlCommand BuildCommand(QueryModel queryModel, out ISelector<TResult> selector);
    }

    abstract class ScalarCommandBuilder<TOperator, TResult> : IScalarCommandBuilder<TResult> where TOperator : ResultOperatorBase
    {
        protected readonly IManagedConnection _runner;
        protected readonly IDocumentSchema _schema;
        protected readonly MartenExpressionParser _expressionParser;

        protected ScalarCommandBuilder(MartenExpressionParser expressionParser, IDocumentSchema schema)
        {
            _expressionParser = expressionParser;
            _schema = schema;
        }

        public bool Match(QueryModel queryModel)
        {
            return queryModel.ResultOperators.OfType<TOperator>().Any();
        }
        public abstract NpgsqlCommand BuildCommand(QueryModel queryModel, out ISelector<TResult> selector);
    }

    abstract class AggregateCommandBuilder<TResultOperator, TResult> : ScalarCommandBuilder<TResultOperator, TResult> where TResultOperator : ResultOperatorBase
    {
        protected AggregateCommandBuilder(MartenExpressionParser expressionParser, IDocumentSchema schema)
            : base(expressionParser, schema)
        { }

        public override NpgsqlCommand BuildCommand(QueryModel queryModel, out ISelector<TResult> selector)
        {
            selector = new ScalarSelector<TResult>();
            var sumCommand = GetCommand(queryModel);
            return sumCommand;
        }

        private NpgsqlCommand GetCommand(QueryModel queryModel)
        {
            var mapping = _schema.MappingFor(queryModel.MainFromClause.ItemType);
            var documentQuery = new DocumentQuery(mapping, queryModel, _expressionParser);

            _schema.EnsureStorageExists(mapping.DocumentType);
            var sumCommand = new NpgsqlCommand();
            ConfigureCommand(documentQuery, sumCommand);
            return sumCommand;
        }

        protected abstract void ConfigureCommand(DocumentQuery documentQuery, NpgsqlCommand command);
    }

    class SumCommandBuilder<TResult> : AggregateCommandBuilder<SumResultOperator, TResult>
    {
        protected override void ConfigureCommand(DocumentQuery documentQuery, NpgsqlCommand command)
        {
            documentQuery.ConfigureForSum(command);
        }

        public SumCommandBuilder(MartenExpressionParser expressionParser, IDocumentSchema schema) 
            : base(expressionParser, schema){}
    }

    class MaxCommandBuilder<TResult> : AggregateCommandBuilder<MaxResultOperator,TResult>
    {
        protected override void ConfigureCommand(DocumentQuery documentQuery, NpgsqlCommand command)
        {
            documentQuery.ConfigureForMax(command);
        }

        public MaxCommandBuilder(MartenExpressionParser expressionParser, IDocumentSchema schema) 
            : base(expressionParser, schema){ }
    }

    class MinCommandBuilder<TResult> : AggregateCommandBuilder<MinResultOperator, TResult>
    {
        protected override void ConfigureCommand(DocumentQuery documentQuery, NpgsqlCommand command)
        {
            documentQuery.ConfigureForMin(command);
        }

        public MinCommandBuilder(MartenExpressionParser expressionParser, IDocumentSchema schema) 
            : base(expressionParser, schema){ }
    }

    class AverageCommandBuilder<TResult> : AggregateCommandBuilder<AverageResultOperator, TResult>
    {
        protected override void ConfigureCommand(DocumentQuery documentQuery, NpgsqlCommand command)
        {
            documentQuery.ConfigureForAverage(command);
        }

        public AverageCommandBuilder(MartenExpressionParser expressionParser, IDocumentSchema schema) 
            : base(expressionParser, schema){ }
    }

    class AnyCommandBuilder<TResult> : ScalarCommandBuilder<AnyResultOperator, TResult>
    {
        public AnyCommandBuilder(MartenExpressionParser expressionParser, IDocumentSchema schema)
            : base(expressionParser, schema){ }

        public override NpgsqlCommand BuildCommand(QueryModel queryModel, out ISelector<TResult> selector)
        {
            selector = new ScalarSelector<TResult>();
            var anyCommand = GetAnyCommand(queryModel);
            return anyCommand;
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

    class CountCommandBuilder<TResult> : ScalarCommandBuilder<CountResultOperator, TResult>
    {
        public CountCommandBuilder(MartenExpressionParser expressionParser, IDocumentSchema schema)
            : base(expressionParser, schema){ }

        public override NpgsqlCommand BuildCommand(QueryModel queryModel, out ISelector<TResult> selector)
        {
            selector = new ScalarSelector<TResult>();
            var countCommand = GetCountCommand(queryModel);
            return countCommand;
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

    class LongCountCommandBuilder<TResult> : ScalarCommandBuilder<LongCountResultOperator, TResult> {
        public LongCountCommandBuilder(MartenExpressionParser expressionParser, IDocumentSchema schema) : base(expressionParser, schema)
        {
        }

        public override NpgsqlCommand BuildCommand(QueryModel queryModel, out ISelector<TResult> selector)
        {
            selector = new ScalarSelector<TResult>();
            var countCommand = GetLongCountCommand(queryModel);
            return countCommand;
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