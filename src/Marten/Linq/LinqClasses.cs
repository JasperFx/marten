using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using FubuCore;
using Remotion.Linq;
using Remotion.Linq.Parsing.ExpressionVisitors.Transformation;
using Remotion.Linq.Parsing.Structure;
using Remotion.Linq.Parsing.Structure.NodeTypeProviders;

namespace Marten.Linq
{
    public class MartenQueryable<T> : QueryableBase<T>
    {
        public MartenQueryable(IQueryParser queryParser, IQueryExecutor executor) : base(queryParser, executor)
        {
        }

        public MartenQueryable(IQueryProvider provider) : base(provider)
        {
        }

        public MartenQueryable(IQueryProvider provider, Expression expression) : base(provider, expression)
        {
        }


    }


    public class QueryProvider : QueryProviderBase
    {
        public QueryProvider(IQueryParser queryParser, IQueryExecutor executor) : base(queryParser, executor)
        {
        }

        public override IQueryable<T> CreateQuery<T>(Expression expression)
        {
            var transformerRegistry = ExpressionTransformerRegistry.CreateDefault();


            var processor = ExpressionTreeParser.CreateDefaultProcessor(transformerRegistry);
            // Add custom processors here:
            // processor.InnerProcessors.Add (new MyExpressionTreeProcessor());


            var expressionTreeParser = new ExpressionTreeParser(MethodInfoBasedNodeTypeRegistry.CreateFromRelinqAssembly(), processor);
            var parser = new QueryParser(expressionTreeParser);

            var model = parser.GetParsedQuery(expression);

            throw new System.NotImplementedException();
        }
    }

    public class QueryExecutor : IQueryExecutor
    {
        public T ExecuteScalar<T>(QueryModel queryModel)
        {
            throw new System.NotImplementedException();
        }

        public T ExecuteSingle<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
        {
            throw new System.NotImplementedException();
        }

        public IEnumerable<T> ExecuteCollection<T>(QueryModel queryModel)
        {
            throw new System.NotImplementedException();
        }
    }

    public static class MartenQueryParser
    {
        private static readonly QueryParser QueryParser;

        static MartenQueryParser()
        {
            var transformerRegistry = ExpressionTransformerRegistry.CreateDefault();


            var processor = ExpressionTreeParser.CreateDefaultProcessor(transformerRegistry);
            // Add custom processors here:
            // processor.InnerProcessors.Add (new MyExpressionTreeProcessor());


            var expressionTreeParser = new ExpressionTreeParser(new MethodInfoBasedNodeTypeRegistry(), processor);
            QueryParser = new QueryParser(expressionTreeParser);
        }

        public static QueryModel Parse(Expression expression)
        {
            return QueryParser.GetParsedQuery(expression);
        }
    }
}