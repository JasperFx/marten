using System;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Linq.Operators;
using Marten.Pagination;
using Remotion.Linq;
using Remotion.Linq.Parsing.ExpressionVisitors.Transformation;
using Remotion.Linq.Parsing.Structure;
using Remotion.Linq.Parsing.Structure.NodeTypeProviders;

namespace Marten.Linq.Parsing
{
    public class MartenQueryParser: IQueryParser
    {
        public static readonly MartenQueryParser Flyweight = new MartenQueryParser(r => r.Register(IncludeExpressionNode.SupportedMethods, typeof(IncludeExpressionNode)));

        public static readonly MartenQueryParser TransformQueryFlyweight = new MartenQueryParser(r =>
        {
            r.Register(ToJsonArrayExpressionNode.SupportedMethods, typeof(ToJsonArrayExpressionNode));
            r.Register(IncludeExpressionNode.SupportedMethods, typeof(IncludeExpressionNode));
            r.Register(StatsExpressionNode.SupportedMethods, typeof(StatsExpressionNode));
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
            try
            {
                return _parser.GetParsedQuery(expressionTreeRoot);
            }
            catch (NotSupportedException e)
            {
                if (e.Message.Contains("PagedList"))
                {
                    throw new BadLinqExpressionException($"The {nameof(PagedListQueryableExtensions.ToPagedList)}() operators cannot be used in compiled queries. Use {nameof(QueryStatistics)} instead.");
                }
                throw;
            }
        }
    }
}
