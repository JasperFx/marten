using System.Linq.Expressions;
using Remotion.Linq;
using Remotion.Linq.Parsing.ExpressionVisitors.Transformation;
using Remotion.Linq.Parsing.Structure;
using Remotion.Linq.Parsing.Structure.NodeTypeProviders;

namespace Marten.Linq
{
    public class MartenQueryParser : IQueryParser
    {
        private readonly QueryParser _parser;

        public MartenQueryParser()
        {
            var transformerRegistry = ExpressionTransformerRegistry.CreateDefault();


            var processor = ExpressionTreeParser.CreateDefaultProcessor(transformerRegistry);

            var expressionTreeParser =
                new ExpressionTreeParser(MethodInfoBasedNodeTypeRegistry.CreateFromRelinqAssembly(), processor);
            _parser = new QueryParser(expressionTreeParser);
        }

        public QueryModel GetParsedQuery(Expression expressionTreeRoot)
        {
            return _parser.GetParsedQuery(expressionTreeRoot);
        }
    }
}