using System;
using System.Linq.Expressions;
using Remotion.Linq;
using Remotion.Linq.Parsing.ExpressionVisitors.Transformation;
using Remotion.Linq.Parsing.Structure;
using Remotion.Linq.Parsing.Structure.NodeTypeProviders;

namespace Marten.Linq
{
    public class MartenQueryParser : IQueryParser
    {
        public static readonly MartenQueryParser Flyweight = new MartenQueryParser();
        public static readonly MartenQueryParser ForCachedQuery = new MartenQueryParser(r=> {
            r.Register(AsJsonExpressionNode.SupportedMethods, typeof (AsJsonExpressionNode));
            r.Register(ToJsonArrayExpressionNode.SupportedMethods, typeof (ToJsonArrayExpressionNode));
        });

        private readonly QueryParser _parser;

        public MartenQueryParser(Action<MethodInfoBasedNodeTypeRegistry> registerNodeTypes = null)
        {
            var transformerRegistry = ExpressionTransformerRegistry.CreateDefault();


            var processor = ExpressionTreeParser.CreateDefaultProcessor(transformerRegistry);

            var nodeTypeRegistry = MethodInfoBasedNodeTypeRegistry.CreateFromRelinqAssembly();
            registerNodeTypes?.Invoke(nodeTypeRegistry);

            var expressionTreeParser =
                new ExpressionTreeParser(nodeTypeRegistry, processor);
            _parser = new QueryParser(expressionTreeParser);
        }

        public QueryModel GetParsedQuery(Expression expressionTreeRoot)
        {
            return _parser.GetParsedQuery(expressionTreeRoot);
        }
    }
}